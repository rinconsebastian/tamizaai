using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MiniExcelLibs;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.UnitTests.Projects;

public sealed class SamplingFrameWorkbookTests : IDisposable
{
    private static readonly XNamespace X = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly FrameFileSource Source = new("Encuesta Antioquia", new DateOnly(2026, 10, 15), "v1");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tamiza-frame-{Guid.NewGuid():N}.xlsx");

    public void Dispose() => File.Delete(_path);

    private static FormField SelectOne(string xpath, string list) => new(xpath.Split('/')[^1], xpath, "select_one", xpath, list);

    private static FormField Typed(string xpath) => new(xpath.Split('/')[^1], xpath, "text", xpath);

    private static readonly FormField[] Fields =
    [
        SelectOne("municipio", "municipios"), SelectOne("sexo", "sexos"), Typed("estrato"), Typed("barrio"), SelectOne("geo/zona", "zonas"),
        new("target", "hogar/target", "integer", "Meta"),
    ];

    private static readonly FormChoice[] Choices =
    [
        new("municipios", "05001", "Medellín"), new("municipios", "05088", "Bello"), new("municipios", "05266", "Envigado"),
        new("sexos", "f", "Femenino"), new("sexos", "m", "Masculino"),
        new("zonas", "u", "Urbana"), new("zonas", "r", "Rural"),
    ];

    private static TemplatePlan Plan(IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, params TemplateDimensionInput[] dimensions)
    {
        var errors = new FieldErrors();
        var plan = SamplingFrameTemplate.Plan(new SamplingFrameTemplateRequest(dimensions.ToList()), fields, choices, errors);
        Assert.False(errors.Any, string.Join("; ", errors.ToDictionary().SelectMany(e => e.Value)));
        return plan!;
    }

    private static TemplateDimensionInput Dim(string field, params string[]? values) => new(field, values?.Select(v => (string?)v).ToList());

    private FrameWorkbookResult Template(params TemplateDimensionInput[] dimensions) =>
        SamplingFrameWorkbook.WriteTemplate(_path, Plan(Fields, Choices, dimensions), Source);

    private List<IDictionary<string, object?>> Sheet(string name) =>
        MiniExcel.Query(_path, useHeaderRow: true, sheetName: name).Cast<IDictionary<string, object?>>().ToList();

    private List<string> Headers(string sheet) =>
        MiniExcel.Query(_path, useHeaderRow: false, sheetName: sheet).Cast<IDictionary<string, object?>>().First().Values.Select(v => v?.ToString() ?? "").ToList();

    private List<string> Guide() => Sheet(SamplingFrameWorkbook.GuideSheet).Select(r => (string)r[SamplingFrameWorkbook.GuideColumn]!).ToList();

    private string Entry(string name)
    {
        using var zip = ZipFile.OpenRead(_path);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private string SheetXml(int number) => Entry($"xl/worksheets/sheet{number}.xml");

    /// <summary>The style indexes whose number format is Text (<c>@</c>).</summary>
    private HashSet<string> TextStyles()
    {
        var styles = XDocument.Parse(Entry("xl/styles.xml"));
        var textFormats = styles.Descendants(X + "numFmt").Where(f => (string?)f.Attribute("formatCode") == "@").Select(f => (string)f.Attribute("numFmtId")!).ToHashSet();
        textFormats.Add("49"); // Excel's built-in Text format
        return styles.Descendants(X + "cellXfs").Elements(X + "xf")
            .Select((xf, i) => (Index: i.ToString(), Format: (string?)xf.Attribute("numFmtId")))
            .Where(xf => xf.Format is not null && textFormats.Contains(xf.Format))
            .Select(xf => xf.Index)
            .ToHashSet();
    }

    private static XElement Cell(string sheetXml, string reference) =>
        XDocument.Parse(sheetXml).Descendants(X + "c").Single(c => (string?)c.Attribute("r") == reference);

    [Fact]
    public void Sheets_are_written_frame_first()
    {
        Template(Dim("municipio", null), Dim("sexo", null));

        Assert.Equal(["Frame", "Guide", "Choices"], MiniExcel.GetSheetNames(_path));
    }

    [Fact]
    public void A_template_has_codes_as_text_label_columns_and_empty_targets()
    {
        var result = Template(Dim("municipio", "05001", "05088"), Dim("sexo", null));

        var frame = Sheet("Frame");
        Assert.True(result.Written);
        Assert.Equal(["municipio", "# municipio label", "sexo", "# sexo label", "target"], Headers("Frame"));
        Assert.Equal(4, frame.Count);
        Assert.Equal("05001", frame[0]["municipio"]);
        Assert.Equal("Medellín", frame[0]["# municipio label"]);
        Assert.Equal(["f", "m", "f", "m"], frame.Select(r => r["sexo"]));
        Assert.Equal("Masculino", frame[1]["# sexo label"]);
        Assert.All(frame, r => Assert.Null(r["target"]));
        var code = Cell(SheetXml(1), "A2");
        Assert.Equal("str", (string?)code.Attribute("t"));
        Assert.Equal("05001", code.Value);
    }

    [Fact]
    public void Choices_lists_every_choice_and_whether_it_is_in_the_file()
    {
        Template(Dim("municipio", "05001", "05088"), Dim("estrato", "1", "2"));

        var choices = Sheet("Choices");
        Assert.Equal(["question column", "choice code", "choice label", "in this file"], Headers("Choices"));
        Assert.Equal(["05001", "05088", "05266"], choices.Select(r => r["choice code"]));
        Assert.Equal(["yes", "yes", "no"], choices.Select(r => r["in this file"]));
        Assert.All(choices, r => Assert.Equal("municipio", r["question column"]));
        Assert.Equal("Envigado", choices[2]["choice label"]);
    }

    [Fact]
    public void The_guide_takes_its_numbers_from_the_rules()
    {
        Template(Dim("municipio", null));

        var guide = string.Join("\n", Guide());
        Assert.Contains(SamplingFrameValidator.MaxTarget.ToString("N0", CultureInfo.InvariantCulture), guide);
        Assert.Contains($"{SamplingFrameValidator.MaxDimensions} questions", guide);
        Assert.Contains($"{SamplingFrameValidator.MaxRows.ToString("N0", CultureInfo.InvariantCulture)} rows", guide);
        Assert.Contains($"{SamplingFrameImport.MaxFileBytes / (1024 * 1024)} MB", guide);
        Assert.Contains("Encuesta Antioquia", guide);
        Assert.Contains("2026-10-15", guide);
        Assert.Contains("form version v1", guide);
        Assert.DoesNotContain("is empty because", guide);
    }

    [Fact]
    public void The_guide_names_an_empty_column()
    {
        Template(Dim("barrio"), Dim("sexo", null));

        Assert.Contains(Guide(), line => line.Contains("'barrio' is empty because no values were typed for it"));
        Assert.All(Sheet("Frame"), r => Assert.Null(r["barrio"]));
    }

    [Fact]
    public void Column_options_apply_to_frame_only_and_every_sheet_has_a_frozen_filtered_header()
    {
        Template(Dim("municipio", null), Dim("sexo", null));

        var frame = SheetXml(1);
        var textStyles = TextStyles();
        Assert.Contains("<x:cols>", frame);
        Assert.DoesNotContain("<x:cols>", SheetXml(2));
        Assert.DoesNotContain("<x:cols>", SheetXml(3));
        Assert.Contains((string)Cell(frame, "A2").Attribute("s")!, textStyles);
        Assert.DoesNotContain((string)Cell(frame, "B2").Attribute("s")!, textStyles);
        Assert.DoesNotContain((string)Cell(SheetXml(3), "B2").Attribute("s")!, textStyles);
        Assert.DoesNotContain((string)Cell(SheetXml(2), "A2").Attribute("s")!, textStyles);
        for (var sheet = 1; sheet <= 3; sheet++)
        {
            Assert.Contains("state=\"frozen\"", SheetXml(sheet));
            Assert.Contains("<x:autoFilter", SheetXml(sheet));
        }
    }

    [Fact]
    public void Questions_named_like_the_other_sheets_columns_write()
    {
        FormField[] fields = [SelectOne("code", "l"), SelectOne("label", "l"), SelectOne("question", "l")];
        FormChoice[] choices = [new("l", "1", "One"), new("l", "2", "Two")];

        var result = SamplingFrameWorkbook.WriteTemplate(_path, Plan(fields, choices, Dim("code", null), Dim("label", null), Dim("question", null)), Source);

        Assert.True(result.Written);
        Assert.Equal(8, Sheet("Frame").Count);
        Assert.Equal(6, Sheet("Choices").Count);
    }

    [Fact]
    public void A_template_without_rows_has_only_its_header()
    {
        var result = Template(Dim("barrio"));

        Assert.True(result.Written);
        Assert.Equal(["barrio", "target"], Headers("Frame"));
        Assert.Empty(Sheet("Frame"));
        Assert.Equal(["question column", "choice code", "choice label", "in this file"], Headers("Choices"));
    }

    [Fact]
    public void A_saved_frame_has_its_targets_as_numbers()
    {
        SamplingDimension[] dimensions = [new("Municipality", "municipio"), new("estrato", "estrato")];
        SamplingTarget[] targets = [new(["05001", "1"], 120), new(["05088", "2"], 0)];

        SamplingFrameWorkbook.WriteFrame(_path, dimensions, targets, Fields, Choices, Source);

        Assert.Equal(["Municipality", "# Municipality label", "estrato", "target"], Headers("Frame"));
        var frame = Sheet("Frame");
        Assert.Equal([120.0, 0.0], frame.Select(r => Convert.ToDouble(r["target"])));
        Assert.Equal("n", (string?)Cell(SheetXml(1), "D2").Attribute("t"));
        Assert.Equal("Bello", frame[1]["# Municipality label"]);
        Assert.Contains("replaces the saved sampling frame", Guide()[^1]);
    }

    [Fact]
    public void A_saved_frame_without_targets_has_only_its_header()
    {
        SamplingFrameWorkbook.WriteFrame(_path, [new("Sexo", "sexo")], [], Fields, Choices, Source);

        Assert.Equal(["Sexo", "# Sexo label", "target"], Headers("Frame"));
        Assert.Empty(Sheet("Frame"));
    }

    [Fact]
    public void Saved_labels_are_found_ignoring_case_and_a_question_without_a_list_gets_no_label_column()
    {
        SamplingDimension[] dimensions = [new("sexo", "sexo"), new("gone", "removed/question")];

        SamplingFrameWorkbook.WriteFrame(_path, dimensions, [new(["F ", "x"], 3)], Fields, Choices, Source);

        Assert.Equal(["sexo", "# sexo label", "gone", "target"], Headers("Frame"));
        Assert.Equal("Femenino", Sheet("Frame")[0]["# sexo label"]);
        Assert.Equal(["yes", "no"], Sheet("Choices").Select(r => r["in this file"]));
    }

    [Fact]
    public void Dimensions_named_like_a_note_or_the_target_are_named_after_their_paths()
    {
        SamplingDimension[] dimensions = [new("#zona", "geo/zona"), new("Target", "hogar/target")];

        SamplingFrameWorkbook.WriteFrame(_path, dimensions, [new(["u", "3"], 10)], Fields, Choices, Source);

        Assert.Equal(["geo/zona", "# geo/zona label", "hogar/target", "target"], Headers("Frame"));
        var guide = Guide();
        Assert.Contains(guide, l => l.Contains("'geo/zona' holds the dimension saved as '#zona'"));
        Assert.Contains(guide, l => l.Contains("'hogar/target' holds the dimension saved as 'Target'"));
    }

    [Fact]
    public void A_renamed_header_that_is_taken_gets_its_position()
    {
        SamplingDimension[] dimensions = [new("geo/zona", "sexo"), new("#zona", "geo/zona"), new("TARGET", "target")];

        var headers = SamplingFrameWorkbook.FrameHeaders(dimensions);

        Assert.Equal(["geo/zona", "geo/zona (2)", "target (3)"], headers);
    }

    [Fact]
    public void Labels_are_cut_on_frame_and_at_the_cell_limit_on_choices()
    {
        FormChoice[] choices = [new("l", "a", new string('x', 250)), new("l", "b", new string('y', 40_000))];

        SamplingFrameWorkbook.WriteTemplate(_path, Plan([SelectOne("q", "l")], choices, Dim("q", null)), Source);

        var frame = Sheet("Frame");
        var label = (string)frame[0]["# q label"]!;
        Assert.Equal(200, label.Length);
        Assert.EndsWith("…", label);
        Assert.Equal(32_767, ((string)Sheet("Choices")[1]["choice label"]!).Length);
    }

    [Fact]
    public void A_cut_label_does_not_end_on_half_a_surrogate_pair()
    {
        var label = new string('x', 198) + "😀" + new string('x', 10);

        var cut = SamplingFrameWorkbook.CutLabel(label);

        Assert.Equal(new string('x', 198) + "…", cut);
    }

    // Size fallback

    private static string Noise(Random random, int length) =>
        new(Enumerable.Range(0, length).Select(_ => (char)random.Next(0x4E00, 0x9FFF)).ToArray());

    [Fact]
    public void A_long_choice_list_is_limited_to_the_chosen_codes()
    {
        var random = new Random(7);
        var choices = Enumerable.Range(0, 40_000).Select(i => new FormChoice("veredas", $"v{i:00000}", Noise(random, 80))).ToArray();
        var chosen = Enumerable.Range(0, 50).Select(i => $"v{i * 7:00000}").ToArray();

        var result = SamplingFrameWorkbook.WriteTemplate(_path, Plan([SelectOne("vereda", "veredas")], choices, Dim("vereda", chosen)), Source);

        Assert.True(result.Written);
        Assert.True(result.ChoicesLimited);
        Assert.False(result.LabelsLeftOut);
        Assert.Equal(chosen, Sheet("Choices").Select(r => (string)r["choice code"]!));
        Assert.Contains(Guide(), l => l.StartsWith("Your form has more choices than the Choices sheet lists"));
        Assert.True(new FileInfo(_path).Length < SamplingFrameWorkbook.MaxBytes);
    }

    [Fact]
    public void A_maximal_template_with_long_labels_stays_importable()
    {
        var random = new Random(11);
        var sizes = new[] { 10, 10, 10, 10, 1, 1, 1, 1, 1, 1 };
        var fields = sizes.Select((_, q) => SelectOne($"q{q}", $"l{q}")).ToArray();
        var choices = sizes.SelectMany((size, q) => Enumerable.Range(0, size).Select(c => new FormChoice($"l{q}", $"c{c}", Noise(random, 1_000)))).ToArray();

        var result = SamplingFrameWorkbook.WriteTemplate(_path, Plan(fields, choices, fields.Select(f => Dim(f.Xpath, null)).ToArray()), Source);

        Assert.True(result.Written);
        Assert.Equal(10_000, Sheet("Frame").Count);
        Assert.True(new FileInfo(_path).Length < SamplingFrameWorkbook.MaxBytes);
    }

    [Fact]
    public void A_template_still_too_large_is_not_written()
    {
        var random = new Random(3);
        var fields = new[] { Typed("slow"), Typed("fast") };
        var slow = Enumerable.Range(0, 100).Select(i => i.ToString()).ToArray();
        var fast = Enumerable.Range(0, 100).Select(_ => Noise(random, 500)).ToArray();

        var result = SamplingFrameWorkbook.WriteTemplate(_path, Plan(fields, [], Dim("slow", slow), Dim("fast", fast)), Source);

        Assert.False(result.Written);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void A_saved_frame_too_large_is_written_and_says_so()
    {
        var random = new Random(5);
        var targets = Enumerable.Range(0, 10_000).Select(i => new SamplingTarget([i.ToString(), Noise(random, 500)], 1)).ToArray();

        var result = SamplingFrameWorkbook.WriteFrame(_path, [new("a", "estrato"), new("b", "barrio")], targets, Fields, Choices, Source);

        Assert.True(result.Written);
        Assert.True(result.TooLarge);
        Assert.Contains(Guide(), l => l.Contains("cannot be imported back as it is"));
    }

    [Fact]
    public void A_written_template_imports_back_with_its_rows_left_out()
    {
        Template(Dim("municipio", "05001", "05088"), Dim("sexo", null));

        using var file = File.OpenRead(_path);
        var (preview, error) = SamplingFrameImport.Read(file, ExcelType.XLSX);

        Assert.Null(error);
        Assert.Equal(["municipio", "sexo"], preview!.Dimensions);
        Assert.Equal([2, 3, 4, 5], preview.RowsWithoutTarget);
        Assert.Equal(["B", "D"], preview.IgnoredColumns.Select(c => c.Column));
        Assert.Empty(preview.Errors);
        Assert.Matches(new Regex("^# municipio label$"), preview.IgnoredColumns[0].Name!);
    }
}
