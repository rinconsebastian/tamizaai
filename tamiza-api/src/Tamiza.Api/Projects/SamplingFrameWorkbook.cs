using System.Data;
using System.Globalization;
using MiniExcelLibs;
using MiniExcelLibs.Attributes;
using MiniExcelLibs.OpenXml;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Performance;

namespace Tamiza.Api.Projects;

/// <summary>Where a workbook comes from, for its Guide sheet.</summary>
public sealed record FrameFileSource(string ProjectName, DateOnly Day, string? FormVersion);

/// <summary>
/// How a workbook was written. A template that stays over <see cref="SamplingFrameWorkbook.MaxBytes"/> after both fallbacks is not
/// written (<see cref="Written"/> is false); a saved frame always is, with <see cref="TooLarge"/> set.
/// </summary>
public sealed record FrameWorkbookResult(bool Written, bool ChoicesLimited, bool LabelsLeftOut, bool TooLarge);

/// <summary>
/// Writes the sampling frame workbook (design D3): <c>Frame</c>, the only sheet the import reads, with a column per question,
/// a <c># … label</c> note column after each question with choices and <c>target</c>; <c>Guide</c>, the rules; and
/// <c>Choices</c>, the codes with their labels. The same layout serves an import template and a saved frame's download.
/// </summary>
public static class SamplingFrameWorkbook
{
    public const string FrameSheet = "Frame";
    public const string GuideSheet = "Guide";
    public const string ChoicesSheet = "Choices";

    // Guide and Choices column names contain a space, so no Frame column option (keyed by name) reaches them (design D3).
    public const string GuideColumn = "How to fill this file";
    public const string QuestionColumn = "question column";
    public const string CodeColumn = "choice code";
    public const string LabelColumn = "choice label";
    public const string InFileColumn = "in this file";

    /// <summary>Labels on Frame are cut to this many characters, so label columns stay readable and the file small.</summary>
    public const int MaxLabelLength = 200;

    /// <summary>Every file stays under this size, so that it can be imported back within the 5 MB limit.</summary>
    public const long MaxBytes = SamplingFrameImport.MaxFileBytes * 9 / 10;

    private const string Ellipsis = "…";

    public static string LabelHeader(string header) => $"{SamplingFrameImport.NotePrefix} {header} label";

    /// <summary>Writes an import template for a checked request: every combination, codes as text and every target empty.</summary>
    public static FrameWorkbookResult WriteTemplate(string path, TemplatePlan plan, FrameFileSource source)
    {
        var columns = plan.Columns.Select(c => new Column(c.Header, c.Choices)).ToList();
        var empty = plan.Columns.Where(c => c.Values.Count == 0).Select(c => c.Header).ToList();
        var content = new Content(columns, () => plan.Rows().Select(values => new Row(values, null)), Template: true, empty, [], source);
        return Write(path, content);
    }

    /// <summary>
    /// Writes a saved frame in the template's layout, with its targets. A dimension the importer would not read back as one (its
    /// name starts with <c>#</c> or is <c>target</c>) is named after its question's path instead, and the Guide says so.
    /// </summary>
    public static FrameWorkbookResult WriteFrame(
        string path, IReadOnlyList<SamplingDimension> dimensions, IReadOnlyList<SamplingTarget> targets, IReadOnlyList<FormField> fields,
        IReadOnlyList<FormChoice> choices, FrameFileSource source)
    {
        var lists = SamplingFrameTemplate.ChoiceLists(fields, choices);
        var headers = FrameHeaders(dimensions);
        var columns = dimensions.Select((d, i) =>
        {
            var field = fields.FirstOrDefault(f => f.Xpath == d.Field);
            return new Column(headers[i], field is null ? null : SamplingFrameTemplate.ChoiceList(field, lists));
        }).ToList();
        var renamed = dimensions.Select((d, i) => (Saved: d.Name, Header: headers[i]))
            .Where(r => r.Header != r.Saved)
            .Select(r => $"The column '{r.Header}' holds the dimension saved as '{r.Saved}': a column with that name would not be read back as a dimension.")
            .ToList();
        var content = new Content(columns, () => targets.Select(t => new Row(t.Values, t.Target)), Template: false, [], renamed, source);
        return Write(path, content);
    }

    /// <summary>
    /// The Frame headers of a saved frame: each dimension's name, or its question's path when the importer would read the name as
    /// a note or as the target column. A path that is <c>target</c> or already another header gets <c> (n)</c>, n being the
    /// dimension's position.
    /// </summary>
    public static IReadOnlyList<string> FrameHeaders(IReadOnlyList<SamplingDimension> dimensions)
    {
        static bool Unreadable(string name) => SamplingFrameImport.IsNote(name) || IsTarget(name);

        var headers = dimensions.Select(d => Unreadable(d.Name) ? null : d.Name).ToArray();
        var taken = new HashSet<string>(headers.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < dimensions.Count; i++)
        {
            if (headers[i] is not null)
            {
                continue;
            }

            var header = dimensions[i].Field;
            if (IsTarget(header) || taken.Contains(header))
            {
                header = $"{header} ({i + 1})";
            }

            headers[i] = header;
            taken.Add(header);
        }

        return headers!;
    }

    /// <summary>Writes the file, then again with fewer choices listed and then without label columns while it is too large.</summary>
    private static FrameWorkbookResult Write(string path, Content content)
    {
        var inFile = content.Columns.Select(_ => new HashSet<FrameChoice>()).ToList();
        foreach (var row in content.Rows())
        {
            for (var i = 0; i < content.Columns.Count; i++)
            {
                if (content.Columns[i].Find(Value(row.Values, i)) is { } choice)
                {
                    inFile[i].Add(choice);
                }
            }
        }

        var hasMoreChoices = content.Columns.Select((c, i) => c.Choices?.Count > inFile[i].Count).Any(more => more);
        var hasLabels = content.Columns.Any(c => c.Choices is not null);
        var attempts = new List<(bool LimitChoices, bool Labels)> { (false, true) };
        if (hasMoreChoices)
        {
            attempts.Add((true, true));
        }

        if (hasLabels)
        {
            attempts.Add((true, false));
        }

        foreach (var (limitChoices, labels) in attempts)
        {
            if (WriteFile(path, content, inFile, limitChoices && hasMoreChoices, labels && hasLabels, tooLarge: false) <= MaxBytes)
            {
                return new FrameWorkbookResult(true, limitChoices && hasMoreChoices, !labels && hasLabels, TooLarge: false);
            }
        }

        if (content.Template)
        {
            ExportRegistry.DeleteQuietly(path);
            return new FrameWorkbookResult(false, hasMoreChoices, hasLabels, TooLarge: true);
        }

        // A saved frame is served anyway; its Guide says that it cannot be imported back as it is.
        WriteFile(path, content, inFile, hasMoreChoices, labels: false, tooLarge: true);
        return new FrameWorkbookResult(true, hasMoreChoices, hasLabels, TooLarge: true);
    }

    private static long WriteFile(string path, Content content, List<HashSet<FrameChoice>> inFile, bool limitChoices, bool labels, bool tooLarge)
    {
        var frameHeaders = new List<string>();
        var dynamicColumns = new List<DynamicExcelColumn>();
        foreach (var column in content.Columns)
        {
            frameHeaders.Add(column.Header);
            dynamicColumns.Add(new DynamicExcelColumn(column.Header) { Width = Math.Clamp(column.Header.Length + 4, 12, 40), Format = "@" });
            if (labels && column.Choices is not null)
            {
                frameHeaders.Add(LabelHeader(column.Header));
                dynamicColumns.Add(new DynamicExcelColumn(LabelHeader(column.Header)) { Width = 30 });
            }
        }

        frameHeaders.Add(SamplingFrameImport.TargetColumn);
        dynamicColumns.Add(new DynamicExcelColumn(SamplingFrameImport.TargetColumn) { Width = 10 });

        var sheets = new Dictionary<string, object>
        {
            [FrameSheet] = Sheet(frameHeaders, FrameRows(content, labels)),
            [GuideSheet] = Sheet([GuideColumn], GuideLines(content, limitChoices, labels, tooLarge).Select(line => Cells((GuideColumn, Cut(line))))),
            [ChoicesSheet] = Sheet([QuestionColumn, CodeColumn, LabelColumn, InFileColumn], ChoiceRows(content, inFile, limitChoices)),
        };

        var configuration = new OpenXmlConfiguration { FastMode = false, EnableAutoWidth = false, DynamicColumns = dynamicColumns.ToArray() };
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            MiniExcel.SaveAs(file, sheets, printHeader: true, excelType: ExcelType.XLSX, configuration: configuration);
        }

        return new FileInfo(path).Length;
    }

    private static IEnumerable<Dictionary<string, object?>> FrameRows(Content content, bool labels)
    {
        foreach (var row in content.Rows())
        {
            var cells = new Dictionary<string, object?>();
            for (var i = 0; i < content.Columns.Count; i++)
            {
                var column = content.Columns[i];
                var value = Value(row.Values, i);
                cells[column.Header] = value.Length == 0 ? null : Cut(value);
                if (labels && column.Choices is not null)
                {
                    cells[LabelHeader(column.Header)] = column.Find(value)?.Label is { Length: > 0 } label ? CutLabel(label) : null;
                }
            }

            cells[SamplingFrameImport.TargetColumn] = row.Target;
            yield return cells;
        }
    }

    private static IEnumerable<Dictionary<string, object?>> ChoiceRows(Content content, List<HashSet<FrameChoice>> inFile, bool limitChoices)
    {
        for (var i = 0; i < content.Columns.Count; i++)
        {
            foreach (var choice in content.Columns[i].Choices ?? [])
            {
                var used = inFile[i].Contains(choice);
                if (limitChoices && !used)
                {
                    continue;
                }

                yield return Cells(
                    (QuestionColumn, content.Columns[i].Header), (CodeColumn, Cut(choice.Code)), (LabelColumn, choice.Label is null ? null : Cut(choice.Label)),
                    (InFileColumn, used ? "yes" : "no"));
            }
        }
    }

    private static List<string> GuideLines(Content content, bool limitChoices, bool labels, bool tooLarge)
    {
        var lines = new List<string>
        {
            $"Only the first sheet, {FrameSheet}, is read when you import this file.",
            $"Each row of {FrameSheet} is one group of the sample. Write in the target column how many surveys you plan for it: a whole number from 0 to {Format(SamplingFrameValidator.MaxTarget)}, without thousands separators.",
            "A row whose target is empty is left out when you import the file. A target of 0 keeps the row.",
            $"Keep the column names. Columns whose name starts with {SamplingFrameImport.NotePrefix} are notes and are not read; you can add your own.",
            $"Write choice codes, not labels. The {ChoicesSheet} sheet lists the codes of each question with their labels.",
            "Before typing a code such as 05001 into a new cell, format the cell as Text, or Excel drops the leading zero. Ignore Excel's green triangle on such cells.",
        };

        if (content.EmptyColumns.Count > 0)
        {
            var names = string.Join(", ", content.EmptyColumns.Select(c => $"'{c}'"));
            lines.Add(content.EmptyColumns.Count == 1
                ? $"The column {names} is empty because no values were typed for it. Fill it in on every row you give a target."
                : $"The columns {names} are empty because no values were typed for them. Fill them in on every row you give a target.");
        }

        lines.Add($"Each combination of values can appear only once. A sampling frame holds at most {SamplingFrameValidator.MaxDimensions} questions and {Format(SamplingFrameValidator.MaxRows)} rows, and an imported file at most {SamplingFrameImport.MaxFileBytes / (1024 * 1024)} MB.");
        lines.Add("Save the file as Excel Workbook (.xlsx) and import it in Settings > Sampling frame.");

        var day = content.Source.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var version = content.Source.FormVersion is { } v ? $"form version {v}" : "the current form";
        lines.Add(content.Template
            ? $"Prepared for the project “{content.Source.ProjectName}” on {day} from {version}. When the form changes, download a new template."
            : $"Downloaded from the project “{content.Source.ProjectName}” on {day}, with {version}.");

        lines.AddRange(content.Renamed);
        if (limitChoices)
        {
            lines.Add($"Your form has more choices than the {ChoicesSheet} sheet lists: it lists only the codes in {FrameSheet}, to keep the file under the {SamplingFrameImport.MaxFileBytes / (1024 * 1024)} MB import limit.");
        }

        if (!labels && content.Columns.Any(c => c.Choices is not null))
        {
            lines.Add("Label columns were left out to keep the file importable.");
        }

        if (tooLarge)
        {
            lines.Add($"This file is larger than the {SamplingFrameImport.MaxFileBytes / (1024 * 1024)} MB import limit, so it cannot be imported back as it is.");
        }

        if (!content.Template)
        {
            lines.Add("Importing this file replaces the saved sampling frame once you review it and press Save.");
        }

        return lines;
    }

    /// <summary>
    /// A sheet's rows, or a table of its columns when there are none: MiniExcel writes no header row for an empty list.
    /// </summary>
    private static object Sheet(IReadOnlyList<string> headers, IEnumerable<Dictionary<string, object?>> rows)
    {
        var list = rows.ToList();
        if (list.Count > 0)
        {
            return list;
        }

        var table = new DataTable();
        foreach (var header in headers)
        {
            table.Columns.Add(header, typeof(string));
        }

        return table;
    }

    private static Dictionary<string, object?> Cells(params (string Column, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Column, c => c.Value);

    private static string Value(IReadOnlyList<string> values, int index) => index < values.Count ? values[index] : "";

    private static string Cut(string text) => XlsxExport.CutToCell(text, out _);

    /// <summary>The label cut to <see cref="MaxLabelLength"/> characters, the last being "…" when it was cut.</summary>
    public static string CutLabel(string label)
    {
        if (label.Length <= MaxLabelLength)
        {
            return label;
        }

        var length = MaxLabelLength - Ellipsis.Length;
        if (char.IsHighSurrogate(label[length - 1]))
        {
            length--;
        }

        return label[..length] + Ellipsis;
    }

    private static string Format(int number) => SamplingFrameValidator.Format(number);

    private static bool IsTarget(string name) => string.Equals(name, SamplingFrameImport.TargetColumn, StringComparison.OrdinalIgnoreCase);

    private sealed record Row(IReadOnlyList<string> Values, int? Target);

    private sealed record Content(
        IReadOnlyList<Column> Columns,
        Func<IEnumerable<Row>> Rows,
        bool Template,
        IReadOnlyList<string> EmptyColumns,
        IReadOnlyList<string> Renamed,
        FrameFileSource Source);

    /// <summary>A Frame column and its choice list, whose codes are found as the dashboard finds answers: trimmed, exact first, then ignoring case.</summary>
    private sealed class Column
    {
        private readonly Dictionary<string, FrameChoice> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FrameChoice> _ignoringCase = new(StringComparer.OrdinalIgnoreCase);

        public Column(string header, IReadOnlyList<FrameChoice>? choices)
        {
            Header = header;
            Choices = choices;
            foreach (var choice in choices ?? [])
            {
                _exact.TryAdd(choice.Code, choice);
                _ignoringCase.TryAdd(choice.Code, choice);
            }
        }

        public string Header { get; }

        public IReadOnlyList<FrameChoice>? Choices { get; }

        public FrameChoice? Find(string value)
        {
            var code = value.Trim(PerformanceResults.TrimChars);
            return _exact.TryGetValue(code, out var choice) || _ignoringCase.TryGetValue(code, out choice) ? choice : null;
        }
    }
}
