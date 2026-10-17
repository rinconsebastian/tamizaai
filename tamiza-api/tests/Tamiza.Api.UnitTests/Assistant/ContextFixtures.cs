using System.Text.Json;
using Tamiza.Api.Assistant;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.UnitTests.Assistant;

/// <summary>A small project, as the context assembler sees it, and builders for requests and tests.</summary>
internal static class ContextFixtures
{
    public static readonly DateTimeOffset Published = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The form: a municipality, a water source with "or other", a select_multiple, a date, a repeat group and a repeat inside it.</summary>
    public static readonly FormField[] Fields =
    [
        new("municipality", "municipality", "select_one_municipalities", "Municipality", "municipalities"),
        new("community", "community", "select_one_communities", "Community", "communities"),
        new("water_source", "water_source", "select_one_water_list", "What is your main water source?", "water_list", OrOther: true),
        new("services", "services", "select_multiple_services_list", "Which services does the household have?", "services_list"),
        new("visit_date", "visit_date", "date", "Visit date"),
        new("member_name", "members/member_name", "text", "Name of the member", InRepeat: true),
        new("age", "members/age", "integer", "Age", InRepeat: true),
        new("child_age", "members/children/child_age", "integer", "Age of the child", InRepeat: true),
    ];

    public static IReadOnlyList<FormChoice> Choices(int communities = 120) =>
    [
        .. new[] { "Piped", "Well", "River", "Rainwater", "Other" }.Select(label => new FormChoice("water_list", label.ToLowerInvariant(), label)),
        .. new[] { "Santa Ana", "Gómez Plata", "Bogotá" }.Select((label, index) => new FormChoice("municipalities", $"m{index}", label)),
        .. new[] { "Water", "Electric power", "Waste water" }.Select((label, index) => new FormChoice("services_list", $"s{index}", label)),
        .. Enumerable.Range(1, communities).Select(number => new FormChoice("communities", $"c{number}", $"Community {number:000}")),
    ];

    public static TableColumn Column(string name, DataColumnType type = DataColumnType.Text, string? label = null) => new(name, type, label);

    public static readonly TableColumn[] RepeatKeys =
    [
        Column("_row_id", DataColumnType.Integer),
        Column("_parent_id", DataColumnType.Integer),
        Column("_parent_table"),
        Column("_index", DataColumnType.Integer),
    ];

    public static InputTable MainTable() => new("main", 1204,
    [
        Column("_id", DataColumnType.Integer),
        Column("_uuid"),
        Column("_submission_time", DataColumnType.Timestamp),
        Column("municipality", label: "Municipality"),
        Column("community", label: "Community"),
        Column("water_source", label: "What is your main water source?"),
        Column("water_source_other", label: "What is your main water source? - Other"),
        Column("services", label: "Which services does the household have?"),
        Column("visit_date", DataColumnType.Date, "Visit date"),
    ]);

    public static InputTable Members() => new("members", 3120,
        [.. RepeatKeys, Column("member_name", label: "Name of the member"), Column("age", DataColumnType.Integer, "Age")]);

    public static InputTable Children() => new("children", 410,
        [.. RepeatKeys, Column("child_age", DataColumnType.Integer, "Age of the child")]);

    public static ScriptInputs Inputs(string kind = "indicator", bool published = true, IReadOnlyList<InputTable>? tables = null) =>
        new(kind == "clean" ? "raw" : "clean",
            published ? Published : null,
            published ? Guid.Parse("11111111-1111-1111-1111-111111111111") : null,
            published ? tables ?? [MainTable(), Members(), Children()] : [],
            [new InputReferenceTable("population", [new InputColumn("municipality", "text"), new InputColumn("people", "integer")])],
            [.. Fields.Select(f => new InputQuestion(f.Name, f.Xpath, f.Type, f.Label))]);

    public static ContextSources Sources(
        string kind = "indicator", bool published = true, string? script1 = "def clean(raw, ctx):\n    return raw\n",
        IReadOnlyList<FormChoice>? choices = null, IReadOnlyList<InputTable>? tables = null, string? enumerator = "group_a/enumerator") =>
        new(Inputs(kind, published, tables), Fields, choices ?? Choices(), "America/Bogota", enumerator, kind == "clean" ? null : script1);

    public const string IndicatorDraft =
        "# Indicator: share of households by municipality.\n"
        + "def compute(cleaned, ctx):\n"
        + "    # %% Rate\n"
        + "    main = cleaned[\"main\"]\n"
        + "    piped = main[\"water_source\"] == \"Piped\"\n"
        + "    return main.assign(value=piped)\n";

    public static ContextRequest Request(
        string kind = "indicator", string action = "script", string? draft = null, IReadOnlyList<AssistantMessage>? messages = null,
        int? focus = null, IndicatorDetails? details = null, ScriptTest? test = null, AssistantWarning? warning = null) =>
        new(kind, kind == "indicator" ? "piped_water_rate" : null, action, messages ?? [new AssistantMessage("user", "share of households with piped water by municipality")],
            draft ?? IndicatorDraft, focus, details, test, warning);

    public static ScriptTest Test(ScriptKind kind, string code, IEnumerable<TestStep> steps, string? name = null) =>
        new()
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            RequestedBy = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Kind = kind,
            Name = name ?? (kind == ScriptKind.Indicator ? "piped_water_rate" : null),
            Code = code,
            SampleSize = 500,
            Status = RunStatus.Failed,
            QueuedAt = Published,
            Steps = [.. steps],
        };

    public static TestStep Step(
        string step, string status = "succeeded", IEnumerable<TestTable>? tables = null, string log = "", string? error = null,
        int? stoppedAt = null, IEnumerable<ShownPreview>? shown = null, IndicatorTestView? dashboard = null) =>
        new(step, status, Published, Published, null, null, [.. tables ?? []], log, error, false, stoppedAt, shown?.ToList(), dashboard);

    public static TestTable Table(string name, long rows, params string[] columns) =>
        new(name, rows, [.. columns.Select(column => new TestColumn(column, column == "value" ? "decimal" : "text"))], 0, null, null);

    /// <summary>The traceback the worker appends to a failed script step: the exception's last line, a blank line, then the traceback.</summary>
    public static string Traceback(string exception, params (int Line, string Function, string Source)[] frames)
    {
        var lines = new List<string> { exception.Split('\n')[^1], "", "Traceback (most recent call last):" };
        foreach (var (line, function, source) in frames)
        {
            lines.Add($"  File \"script.py\", line {line}, in {function}");
            lines.Add($"    {source}");
            lines.Add($"    {new string('^', source.Length)}");
        }

        lines.Add(exception);
        return string.Join('\n', lines);
    }

    /// <summary>The content of a tagged block of the system message.</summary>
    public static string Block(AssembledContext context, string tag)
    {
        var open = $"<{tag}>\n";
        var start = context.System.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The system message has no <{tag}> block.");
        var end = context.System.IndexOf($"\n</{tag}>", start, StringComparison.Ordinal);
        return context.System[(start + open.Length)..end];
    }

    public static IReadOnlyDictionary<string, JsonElement> Params(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
