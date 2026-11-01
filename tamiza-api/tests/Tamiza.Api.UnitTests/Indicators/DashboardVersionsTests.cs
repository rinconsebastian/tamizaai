using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>What a version holds and lists, and when the newest one grows (dashboard-versions, design D2 and D4).</summary>
public sealed class DashboardVersionsTests
{
    private static readonly Guid Analyst = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Saved = new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);

    private static DashboardChange Added(string widget) => new(DashboardChangeKinds.WidgetAdded, Widget: widget);

    private static readonly DashboardChange Moved = new(DashboardChangeKinds.LayoutChanged);

    private static DashboardVersion Newest(
        DashboardVersionOrigin origin = DashboardVersionOrigin.Mcp, string? note = "Add five water indicators", Guid? author = null,
        string changes = """[{"kind": "widgetAdded", "widget": "Coverage"}]""", int? restoredFrom = null) => new()
    {
        Number = 4,
        Content = "{}",
        Changes = changes,
        Origin = origin,
        Note = note,
        AuthorId = author ?? Analyst,
        RestoredFrom = restoredFrom,
        CreatedAt = Saved,
        SavedAt = Saved,
    };

    private static bool Grows(DashboardVersion newest, string? note = "Add five water indicators", Guid? author = null,
        DashboardVersionOrigin origin = DashboardVersionOrigin.Mcp, TimeSpan? after = null) =>
        DashboardVersions.Grows(newest, origin, author ?? Analyst, note, Saved + (after ?? TimeSpan.FromMinutes(5)));

    // Changes listed (design D2)

    [Fact]
    public void A_version_lists_at_most_20_changes_and_counts_the_others()
    {
        var nineteen = Enumerable.Range(1, 19).Select(i => Added($"W{i}")).ToList();

        var (changes, more) = DashboardVersions.Append(nineteen, 0, [Added("W20"), Added("W21"), Added("W22")]);
        var (later, evenMore) = DashboardVersions.Append(changes, more, [Moved]);

        Assert.Equal(20, changes.Count);
        Assert.Equal(Added("W20"), changes[^1]);
        Assert.Equal(2, more);
        Assert.Equal(changes, later);
        Assert.Equal(3, evenMore);
    }

    [Fact]
    public void A_change_equal_to_the_last_one_listed_is_not_listed_again()
    {
        var (moves, none) = DashboardVersions.Append([Moved], 0, [Moved, Moved, Added("Coverage"), Added("Coverage"), Moved]);

        Assert.Equal([Moved, Added("Coverage"), Moved], moves);
        Assert.Equal(0, none);
        // Two widgets with other titles are two changes, and a repeat at the cap is not counted either.
        Assert.Equal([Added("A"), Added("B")], DashboardVersions.Append([], 0, [Added("A"), Added("B")]).Changes);
        var full = Enumerable.Range(1, 19).Select(i => Added($"W{i}")).Append(Moved).ToList();
        var (same, counted) = DashboardVersions.Append(full, 1, [Moved]);
        Assert.Equal(full, same);
        Assert.Equal(1, counted);
    }

    [Fact]
    public void A_change_leaves_out_the_fields_its_kind_does_not_use_and_reads_back_as_written()
    {
        var changes = new[]
        {
            new DashboardChange(DashboardChangeKinds.Created),
            new DashboardChange(DashboardChangeKinds.WidgetMovedOut, Widget: "Coverage", Dashboard: "Field"),
            new DashboardChange(DashboardChangeKinds.Restored, Version: 7),
        };

        var json = JsonSerializer.Serialize(changes, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("""[{"kind":"created"},{"kind":"widgetMovedOut","widget":"Coverage","dashboard":"Field"},{"kind":"restored","version":7}]""", json);
        Assert.Equal(changes, DashboardVersions.ReadChanges(json));
        Assert.Equal(changes, DashboardVersions.ReadChanges("""[{"kind": "created"}, {"kind": "widgetMovedOut", "widget": "Coverage", "dashboard": "Field"}, {"kind": "restored", "version": 7}]"""));
    }

    // One version per request of an AI client (design D4)

    [Fact]
    public void The_same_request_of_the_same_person_through_mcp_grows_the_newest_version()
    {
        Assert.True(Grows(Newest()));
        Assert.True(Grows(Newest(), after: TimeSpan.FromMinutes(29)));
    }

    [Theory]
    [InlineData("add five water indicators")]
    [InlineData("  ADD FIVE WATER INDICATORS  ")]
    [InlineData("Add five water indicators\n")]
    public void A_request_that_differs_only_in_case_and_surrounding_spaces_is_the_same(string note) => Assert.True(Grows(Newest(), note));

    [Fact]
    public void Another_request_another_person_a_pause_of_31_minutes_or_a_change_made_in_tamiza_makes_a_new_version()
    {
        Assert.False(Grows(Newest(), "Rename Water to Agua"));
        Assert.False(Grows(Newest(), "Add five water indicators."));
        Assert.False(Grows(Newest(), author: Other));
        Assert.False(Grows(Newest(), after: TimeSpan.FromMinutes(31)));
        Assert.False(Grows(Newest(), after: DashboardVersions.RequestWindow));
        Assert.False(Grows(Newest(), note: null, origin: DashboardVersionOrigin.App));
        Assert.False(Grows(Newest(origin: DashboardVersionOrigin.App, note: null)));
        Assert.False(Grows(Newest(origin: DashboardVersionOrigin.App, note: null), note: null, origin: DashboardVersionOrigin.App));
    }

    [Theory]
    [InlineData("""[{"kind": "initial"}]""", null)]
    [InlineData("""[{"kind": "unrecorded"}]""", null)]
    [InlineData("""[{"kind": "restored", "version": 3}]""", 3)]
    [InlineData("""[{"kind": "widgetAdded", "widget": "Coverage"}]""", 3)]
    public void The_state_before_history_an_unrecorded_state_and_a_restore_never_grow(string changes, int? restoredFrom) =>
        Assert.False(Grows(Newest(changes: changes, restoredFrom: restoredFrom)));

    [Fact]
    public void The_window_is_30_minutes_and_a_dashboard_keeps_10_versions()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), DashboardVersions.RequestWindow);
        Assert.Equal(10, DashboardVersions.Kept);
        Assert.Equal(20, DashboardVersions.MaxChanges);
    }

    // Content (design D2)

    private static DashboardWidget Widget(string id, string title, string layout, DateTimeOffset created, string config = """{"indicator":"coverage"}""") => new()
    {
        Id = Guid.Parse(id),
        Title = title,
        ChartType = "stat",
        Config = config,
        Layout = layout,
        CreatedAt = created,
    };

    [Fact]
    public void The_widgets_are_in_reading_order_compared_as_numbers_with_the_creation_and_the_id_breaking_ties()
    {
        var early = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var widgets = new[]
        {
            Widget("00000000-0000-0000-0000-0000000000c1", "Last", """{"x":0,"y":10,"w":12,"h":2}""", early),
            Widget("00000000-0000-0000-0000-0000000000c6", "Tie, larger id", """{"x":2,"y":4,"w":3,"h":2}""", early),
            Widget("00000000-0000-0000-0000-0000000000c5", "Tie, smaller id", """{"x":2,"y":4,"w":3,"h":2}""", early),
            Widget("00000000-0000-0000-0000-0000000000c4", "Newer", """{"x":10,"y":2,"w":2,"h":2}""", early.AddHours(1)),
            Widget("00000000-0000-0000-0000-0000000000c7", "Older", """{"x":10,"y":2,"w":2,"h":2}""", early),
            Widget("00000000-0000-0000-0000-0000000000c3", "First", """{"x":9,"y":0,"w":3,"h":2}""", early.AddDays(1)),
            Widget("00000000-0000-0000-0000-0000000000c2", "Left of older", """{"x":9,"y":2,"w":1,"h":2}""", early.AddDays(1)),
        };

        var content = JsonDocument.Parse(DashboardVersions.Content("Housing", "[]", widgets)).RootElement;

        Assert.Equal(
            ["First", "Left of older", "Older", "Newer", "Tie, smaller id", "Tie, larger id", "Last"],
            content.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("title").GetString()!).ToArray());
    }

    [Fact]
    public void The_filters_the_config_and_the_layout_are_embedded_as_stored()
    {
        const string filters = """[{"column":"municipality","label":"Municipality","kind":"text","default":{"values":["Rionegro"]}}]""";
        // Saved before the series settings existed, with a key no current field reads.
        const string config = """{"indicator":"households","measure":"value","legacy":true}""";
        var widget = Widget("00000000-0000-0000-0000-0000000000c1", "Households <b>", """{"x":0,"y":0,"w":3,"h":2}""", Saved, config);

        var content = DashboardVersions.Content("Housing", filters, [widget]);

        Assert.Equal(
            """{"name":"Housing","filters":[{"column":"municipality","label":"Municipality","kind":"text","default":{"values":["Rionegro"]}}],"widgets":""" +
            """[{"id":"00000000-0000-0000-0000-0000000000c1","title":"Households \u003Cb\u003E","chartType":"stat","config":""" +
            """{"indicator":"households","measure":"value","legacy":true},"layout":{"x":0,"y":0,"w":3,"h":2}}]}""",
            content);
        Assert.Equal("Households <b>", JsonDocument.Parse(content).RootElement.GetProperty("widgets")[0].GetProperty("title").GetString());
    }

    [Fact]
    public void A_new_projects_dashboard_starts_with_version_1_by_its_creator()
    {
        var dashboard = new Dashboard
        {
            Id = Guid.CreateVersion7(),
            ProjectId = Guid.CreateVersion7(),
            Name = "Indicators",
            NormalizedName = "indicators",
            CreatedBy = Analyst,
            CreatedAt = Saved,
            UpdatedBy = Analyst,
            UpdatedAt = Saved,
        };

        var version = DashboardVersions.FirstVersion(dashboard);

        Assert.Equal((dashboard.ProjectId, dashboard.Id, 1, Analyst, Saved, Saved), (version.ProjectId, version.DashboardId, version.Number, version.AuthorId, version.CreatedAt, version.SavedAt));
        Assert.Equal("""{"name":"Indicators","filters":[],"widgets":[]}""", version.Content);
        Assert.Equal("""[{"kind":"created"}]""", version.Changes);
        Assert.Equal((DashboardVersionOrigin.App, null, 0, null), (version.Origin, version.Note, version.MoreChanges, version.RestoredFrom));
    }
}
