using System.Globalization;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Performance;

namespace Tamiza.Api.Projects;

/// <summary>One chosen question: its path, and the codes or values its rows take (<c>null</c> for every choice, or no values).</summary>
public sealed record TemplateDimensionInput(string? Field, List<string?>? Values);

public sealed record SamplingFrameTemplateRequest(List<TemplateDimensionInput>? Dimensions);

/// <summary>A choice of a question's choice list.</summary>
public sealed record FrameChoice(string Code, string? Label);

/// <summary>
/// A column of the file: the question, the column's name, the question's choice list (null when it has none) and the values of
/// the rows, in the order they combine.
/// </summary>
public sealed record TemplateColumn(FormField Field, string Header, IReadOnlyList<FrameChoice>? Choices, IReadOnlyList<string> Values);

/// <summary>The checked request: the columns in request order and the number of rows they make.</summary>
public sealed record TemplatePlan(IReadOnlyList<TemplateColumn> Columns, long RowCount)
{
    /// <summary>Every combination of the non-empty value lists, the first column varying slowest; an empty list stays blank.</summary>
    public IEnumerable<string[]> Rows() => SamplingFrameTemplate.Combinations(Columns.Select(c => c.Values).ToList());
}

/// <summary>
/// The rules of an import template request (design D7): which questions can be chosen, their choice lists, the column names and
/// the rows the chosen values make. The UI mirrors these rules in <c>sampling-frame-fields.ts</c>; this side is the authority.
/// </summary>
public static class SamplingFrameTemplate
{
    public const int MaxValueLength = 500;

    /// <summary>The code pyxform adds to the list of an or_other question when it deploys the form.</summary>
    public const string OtherCode = "other";

    public const string OtherLabel = "Other";

    /// <summary>Row counts are printed exactly up to this number, and as "more than" it past it.</summary>
    private const long MaxPrintedRows = 1_000_000;

    /// <summary>Checks the request against the form snapshot. Returns the plan, or null with the problems in <paramref name="errors"/>.</summary>
    public static TemplatePlan? Plan(
        SamplingFrameTemplateRequest request, IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, FieldErrors errors)
    {
        var inputs = request.Dimensions ?? [];
        if (inputs.Count is 0 or > SamplingFrameValidator.MaxDimensions)
        {
            errors.Add("dimensions", $"Choose between 1 and {SamplingFrameValidator.MaxDimensions} questions.");
            return null;
        }

        var lists = ChoiceLists(fields, choices);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<TemplateColumn?>();
        for (var i = 0; i < inputs.Count; i++)
        {
            columns.Add(Column(inputs[i], i, fields, lists, chosen, errors));
        }

        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i] is { } column && !headers.TryAdd(column.Header, i))
            {
                errors.Add($"dimensions[{i}].field",
                    $"'{column.Field.Xpath}' and '{columns[headers[column.Header]]!.Field.Xpath}' would both be named '{column.Header}' in the file. Choose one of them.");
            }
        }

        if (errors.Any)
        {
            return null;
        }

        var valid = columns.Select(c => c!).ToList();
        var rowCount = CountRows(valid.Select(c => c.Values.Count));
        if (rowCount > SamplingFrameValidator.MaxRows)
        {
            var count = rowCount > MaxPrintedRows
                ? $"more than {MaxPrintedRows.ToString("N0", CultureInfo.InvariantCulture)}"
                : rowCount.ToString("N0", CultureInfo.InvariantCulture);
            errors.Add("dimensions",
                $"These choices make {count} rows; a sampling frame holds at most {SamplingFrameValidator.Format(SamplingFrameValidator.MaxRows)}. Choose fewer choices or questions.");
            return null;
        }

        return new TemplatePlan(valid, rowCount);
    }

    private static TemplateColumn? Column(
        TemplateDimensionInput input, int index, IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, IReadOnlyList<FrameChoice>> lists,
        HashSet<string> chosen, FieldErrors errors)
    {
        var key = $"dimensions[{index}].field";
        var xpath = input.Field?.Trim() ?? "";
        if (xpath.Length == 0)
        {
            errors.Add(key, "Choose a question.");
            return null;
        }

        var field = fields.FirstOrDefault(f => f.Xpath == xpath);
        if (field is null)
        {
            errors.Add(key, $"The form has no question '{xpath}'.");
            return null;
        }

        if (!chosen.Add(xpath))
        {
            errors.Add(key, $"'{xpath}' is chosen more than once.");
            return null;
        }

        if (Issue(field) is { } issue)
        {
            errors.Add(key, issue);
            return null;
        }

        var header = Header(field, fields);
        var choiceList = ChoiceList(field, lists);
        var values = choiceList is null
            ? TypedValues(input.Values, field, $"dimensions[{index}].values", errors)
            : ChosenCodes(input.Values, field, choiceList, lists[field.ListName!], $"dimensions[{index}].values", errors);
        return values is null ? null : new TemplateColumn(field, header, choiceList, values);
    }

    /// <summary>Why the guided path does not offer a question (design D8), or null when it does.</summary>
    public static string? Issue(FormField field)
    {
        if (field.Type.StartsWith("select_multiple", StringComparison.Ordinal))
        {
            return $"Coverage cannot count '{field.Name}': an answer can hold several choices.";
        }

        if (field.InRepeat)
        {
            return $"Coverage cannot count '{field.Name}': it is answered once per repeat.";
        }

        return IsTarget(field.Xpath)
            ? $"'{field.Xpath}' cannot be used: its name is reserved for the target column."
            : null;
    }

    /// <summary>
    /// The column name of a question: its name, or its path when another field of the form has the same name (notes and metadata
    /// count) or when the name is <c>target</c>, ignoring case. XLSForm names and paths never start with <c>#</c>.
    /// </summary>
    public static string Header(FormField field, IReadOnlyList<FormField> snapshot)
    {
        var shared = snapshot.Any(f => f.Xpath != field.Xpath && string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase));
        return shared || IsTarget(field.Name) ? field.Xpath : field.Name;
    }

    /// <summary>The list's own choices, by list name: codes trimmed and kept once ignoring case, the first in form order winning.</summary>
    public static Dictionary<string, IReadOnlyList<FrameChoice>> ChoiceLists(IReadOnlyList<FormField> fields, IEnumerable<FormChoice> choices)
    {
        var used = fields.Where(f => f.ListName is not null).Select(f => f.ListName!).ToHashSet(StringComparer.Ordinal);
        var lists = new Dictionary<string, List<FrameChoice>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            if (!used.Contains(choice.ListName))
            {
                continue;
            }

            if (!lists.TryGetValue(choice.ListName, out var list))
            {
                lists[choice.ListName] = list = [];
                seen[choice.ListName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var code = choice.Name.Trim(PerformanceResults.TrimChars);
            if (code.Length > 0 && seen[choice.ListName].Add(code))
            {
                list.Add(new FrameChoice(code, choice.Label));
            }
        }

        return lists.ToDictionary(l => l.Key, l => (IReadOnlyList<FrameChoice>)l.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// The choices a question offers, or null when it has none: a <c>select_one</c> question (not from a file) whose list has at
    /// least one choice. An or_other question also offers <c>other</c>, unless its list already has that code.
    /// </summary>
    public static IReadOnlyList<FrameChoice>? ChoiceList(FormField field, IReadOnlyDictionary<string, IReadOnlyList<FrameChoice>> lists)
    {
        if (!HasChoices(field, lists))
        {
            return null;
        }

        var own = lists[field.ListName!];
        return field.OrOther && !own.Any(c => string.Equals(c.Code, OtherCode, StringComparison.OrdinalIgnoreCase))
            ? [.. own, new FrameChoice(OtherCode, OtherLabel)]
            : own;
    }

    public static bool HasChoices(FormField field, IReadOnlyDictionary<string, IReadOnlyList<FrameChoice>> lists) =>
        field.Type.StartsWith("select_one", StringComparison.Ordinal)
        && !field.Type.StartsWith("select_one_from_file", StringComparison.Ordinal)
        && field.ListName is not null
        && lists.TryGetValue(field.ListName, out var list) && list.Count > 0;

    /// <summary>
    /// The codes chosen for a question with choices, in form order: null means the list's own choices (without an added
    /// <c>other</c>). Each value must be a code of the list, matched exactly and then ignoring case.
    /// </summary>
    private static List<string>? ChosenCodes(
        List<string?>? values, FormField field, IReadOnlyList<FrameChoice> choiceList, IReadOnlyList<FrameChoice> own, string key, FieldErrors errors)
    {
        if (values is null)
        {
            return own.Select(c => c.Code).ToList();
        }

        if (values.Count == 0)
        {
            errors.Add(key, "Choose at least one choice.");
            return null;
        }

        var positions = choiceList.Select((c, i) => (c.Code, i)).ToList();
        var picked = new SortedSet<int>();
        foreach (var raw in values)
        {
            var value = raw?.Trim(PerformanceResults.TrimChars) ?? "";
            var match = positions.FindIndex(p => p.Code == value);
            if (match < 0)
            {
                match = positions.FindIndex(p => string.Equals(p.Code, value, StringComparison.OrdinalIgnoreCase));
            }

            if (match < 0)
            {
                errors.Add(key, $"'{value}' is not a choice of '{field.Name}'.");
                return null;
            }

            picked.Add(match);
        }

        return picked.Select(i => choiceList[i].Code).ToList();
    }

    /// <summary>Values typed for a question without choices: trimmed, blank ones dropped, kept once ignoring case.</summary>
    private static List<string>? TypedValues(List<string?>? values, FormField field, string key, FieldErrors errors)
    {
        var normalized = NormalizeValues(values ?? []);
        if (normalized.Any(v => v.Length > MaxValueLength))
        {
            errors.Add(key, $"A value of '{field.Name}' is longer than {MaxValueLength} characters.");
            return null;
        }

        return normalized;
    }

    public static List<string> NormalizeValues(IEnumerable<string?> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return values
            .Select(v => v?.Trim(PerformanceResults.TrimChars) ?? "")
            .Where(v => v.Length > 0 && seen.Add(v))
            .ToList();
    }

    /// <summary>0 when no list has values, otherwise the product of the non-empty lengths, saturating at <see cref="long.MaxValue"/>.</summary>
    public static long CountRows(IEnumerable<int> lengths)
    {
        long count = 0;
        foreach (var length in lengths.Where(l => l > 0))
        {
            count = count == 0 ? length : count > long.MaxValue / length ? long.MaxValue : count * length;
        }

        return count;
    }

    /// <summary>The cartesian product of the non-empty lists, the first varying slowest; an empty list's cell stays blank.</summary>
    public static IEnumerable<string[]> Combinations(IReadOnlyList<IReadOnlyList<string>> lists)
    {
        if (lists.All(l => l.Count == 0))
        {
            yield break;
        }

        var positions = new int[lists.Count];
        while (true)
        {
            yield return lists.Select((l, i) => l.Count == 0 ? "" : l[positions[i]]).ToArray();

            var column = lists.Count - 1;
            for (; column >= 0; column--)
            {
                if (lists[column].Count == 0)
                {
                    continue;
                }

                if (++positions[column] < lists[column].Count)
                {
                    break;
                }

                positions[column] = 0;
            }

            if (column < 0)
            {
                yield break;
            }
        }
    }

    private static bool IsTarget(string name) => string.Equals(name, SamplingFrameImport.TargetColumn, StringComparison.OrdinalIgnoreCase);
}
