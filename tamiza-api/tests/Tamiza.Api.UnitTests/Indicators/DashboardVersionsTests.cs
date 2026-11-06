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

    private static DashboardWidget Widget(
        string id, string title, string layout, DateTimeOffset created, string config = """{"indicator":"coverage"}""", string chartType = "stat") => new()
    {
        Id = Guid.Parse(id),
        Title = title,
        ChartType = chartType,
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

        var content = JsonDocument.Parse(DashboardVersions.Content("Housing", "[]", "tamiza", "[]", null, null, false, widgets)).RootElement;

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

        var content = DashboardVersions.Content("Housing", filters, "tamiza", "[]", null, null, false, [widget]);

        Assert.Equal(
            """{"name":"Housing","filters":[{"column":"municipality","label":"Municipality","kind":"text","default":{"values":["Rionegro"]}}],"widgets":""" +
            """[{"id":"00000000-0000-0000-0000-0000000000c1","title":"Households \u003Cb\u003E","chartType":"stat","config":""" +
            """{"indicator":"households","measure":"value","legacy":true},"layout":{"x":0,"y":0,"w":3,"h":2}}]}""",
            content);
        Assert.Equal("Households <b>", JsonDocument.Parse(content).RootElement.GetProperty("widgets")[0].GetProperty("title").GetString());
    }

    // The palette and the colors kept by value (design D6 of dashboard palettes)

    private const string HousingWidgetsJson =
        """[{"id":"00000000-0000-0000-0000-0000000000a1","title":"Households","chartType":"stat","config":{"indicator":"households","measure":"value","legacy":true},"layout":{"x":0,"y":0,"w":3,"h":2}},""" +
        """{"id":"00000000-0000-0000-0000-0000000000a2","title":"Coverage","chartType":"column","config":{"indicator":"coverage","groupBy":"municipality","seriesBy":null},"layout":{"x":3,"y":0,"w":9,"h":4}},""" +
        """{"id":"00000000-0000-0000-0000-0000000000a3","title":"Water source","chartType":"pie","config":{"indicator":"water","groupBy":"source"},"layout":{"x":0,"y":4,"w":6,"h":4}}]""";

    /// <summary>
    /// The content of a dashboard migrated from script 0018, as its version 1 holds it and as this build gave it before palettes existed:
    /// three widgets, the first with a key no field reads. A fixed fixture: it is never built from the code under test.
    /// </summary>
    private const string BeforePalettes =
        """{"name":"Housing","filters":[{"column":"municipality","label":"Municipality","kind":"text","default":{"values":["Rionegro"]}}],"widgets":""" +
        HousingWidgetsJson + "}";

    private const string FiltersJson = """[{"column":"municipality","label":"Municipality","kind":"text","default":{"values":["Rionegro"]}}]""";

    private const string TwoColors = """[{"column":"sex","value":"Female","color":5},{"column":"answer","value":"No","color":8}]""";

    private static DashboardWidget[] HousingWidgets() =>
    [
        Widget("00000000-0000-0000-0000-0000000000a3", "Water source", """{"x":0,"y":4,"w":6,"h":4}""", Saved, """{"indicator":"water","groupBy":"source"}""", "pie"),
        Widget("00000000-0000-0000-0000-0000000000a1", "Households", """{"x":0,"y":0,"w":3,"h":2}""", Saved, """{"indicator":"households","measure":"value","legacy":true}"""),
        Widget("00000000-0000-0000-0000-0000000000a2", "Coverage", """{"x":3,"y":0,"w":9,"h":4}""", Saved,
            """{"indicator":"coverage","groupBy":"municipality","seriesBy":null}""", "column"),
    ];

    [Fact]
    public void A_dashboard_with_the_default_colors_has_the_content_it_had_before_palettes_byte_for_byte()
    {
        var content = DashboardVersions.Content("Housing", FiltersJson, Palettes.Tamiza, "[]", null, null, false, HousingWidgets());

        Assert.Equal(BeforePalettes, content);
        Assert.DoesNotContain("palette", content);
        Assert.DoesNotContain("valueColors", content);
        // Whatever spacing PostgreSQL gave the empty list it stores.
        Assert.Equal(BeforePalettes, DashboardVersions.Content("Housing", FiltersJson, "tamiza", " [ ] ", null, null, false, HousingWidgets()));
    }

    [Fact]
    public void A_content_recorded_before_palettes_reads_as_the_palette_tamiza_with_no_colors_kept()
    {
        var content = DashboardVersions.ReadContent(BeforePalettes);

        Assert.Equal(("Housing", FiltersJson, "tamiza", "[]"), (content.Name, content.Filters, content.Palette, content.ValueColors));
        Assert.Equal(["Households", "Coverage", "Water source"], content.Widgets.Select(w => w.Title).ToArray());
        Assert.Equal("""{"indicator":"households","measure":"value","legacy":true}""", content.Widgets[0].Config);
    }

    [Fact]
    public void A_palette_and_two_colors_kept_are_written_after_the_filters_and_read_back_the_same()
    {
        var content = DashboardVersions.Content("Housing", FiltersJson, Palettes.Earth, TwoColors, null, null, false, HousingWidgets());

        Assert.Equal(["name", "filters", "palette", "valueColors", "widgets"], JsonDocument.Parse(content).RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(
            """{"name":"Housing","filters":""" + FiltersJson + ""","palette":"earth","valueColors":""" + TwoColors + ""","widgets":""" +
            HousingWidgetsJson + "}",
            content);
        var read = DashboardVersions.ReadContent(content);
        Assert.Equal(("Housing", FiltersJson, "earth", TwoColors), (read.Name, read.Filters, read.Palette, read.ValueColors));
        Assert.Equal(["Households", "Coverage", "Water source"], read.Widgets.Select(w => w.Title).ToArray());
        // And again: what was read builds the same content.
        Assert.Equal(content, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source, read.MinN, read.ShowN,
            read.Widgets.Select(w => Widget(w.Id.ToString(), w.Title, w.Layout, Saved, w.Config, w.ChartType))));
    }

    [Theory]
    [InlineData("earth", "[]", """{"name":"A","filters":[],"palette":"earth","widgets":[]}""")]
    [InlineData("tamiza", TwoColors, """{"name":"A","filters":[],"valueColors":""" + TwoColors + ""","widgets":[]}""")]
    [InlineData("vivid", TwoColors, """{"name":"A","filters":[],"palette":"vivid","valueColors":""" + TwoColors + ""","widgets":[]}""")]
    [InlineData("tamiza", "[]", """{"name":"A","filters":[],"widgets":[]}""")]
    public void Each_of_the_palette_and_the_colors_is_written_only_when_it_is_not_the_default(string palette, string colors, string expected)
    {
        var content = DashboardVersions.Content("A", "[]", palette, colors, null, null, false, []);

        Assert.Equal(expected, content);
        var read = DashboardVersions.ReadContent(content);
        Assert.Equal((palette, JsonDocument.Parse(colors).RootElement.GetRawText()), (read.Palette, read.ValueColors));
    }

    [Theory]
    [InlineData("""{"name":"A","filters":[],"palette":"neon","widgets":[]}""")]
    [InlineData("""{"name":"A","filters":[],"palette":7,"valueColors":{},"widgets":[]}""")]
    [InlineData("""{"name":"A","filters":[],"palette":null,"valueColors":"[]","widgets":[]}""")]
    public void A_palette_the_catalog_does_not_have_and_a_value_that_is_not_a_list_read_as_the_defaults(string json)
    {
        var read = DashboardVersions.ReadContent(json);

        Assert.Equal(("tamiza", "[]"), (read.Palette, read.ValueColors));
    }

    // The source (design D7 of the widget context)

    /// <summary>
    /// The content of a dashboard with a palette and two colors kept, as this build gave it before sources existed and as a dashboard
    /// migrated from script 0021 holds it. A fixed fixture: it is never built from the code under test.
    /// </summary>
    private const string FromScript0021 =
        """{"name":"Housing","filters":[{"column":"municipality","label":"Municipality","kind":"text","default":{"values":["Rionegro"]}}]""" +
        ""","palette":"earth","valueColors":[{"column":"sex","value":"Female","color":5},{"column":"answer","value":"No","color":8}],"widgets":""" +
        HousingWidgetsJson + "}";

    private const string SurveySource = "Encuesta de ejemplo, 120 respuestas, primera ronda";

    [Fact]
    public void A_dashboard_without_a_source_has_the_content_it_had_before_sources_byte_for_byte()
    {
        var content = DashboardVersions.Content("Housing", FiltersJson, Palettes.Earth, TwoColors, null, null, false, HousingWidgets());

        Assert.Equal(FromScript0021, content);
        Assert.DoesNotContain("\"source\":", content);
        Assert.Equal(BeforePalettes, DashboardVersions.Content("Housing", FiltersJson, Palettes.Tamiza, "[]", null, null, false, HousingWidgets()));
    }

    [Fact]
    public void A_content_recorded_before_sources_reads_as_having_none_and_builds_the_same_content_again()
    {
        var read = DashboardVersions.ReadContent(FromScript0021);

        Assert.Null(read.Source);
        Assert.Equal(("earth", TwoColors), (read.Palette, read.ValueColors));
        Assert.Equal(FromScript0021, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source, read.MinN, read.ShowN,
            read.Widgets.Select(w => Widget(w.Id.ToString(), w.Title, w.Layout, Saved, w.Config, w.ChartType))));
        Assert.Null(DashboardVersions.ReadContent(BeforePalettes).Source);
    }

    [Fact]
    public void A_source_is_written_after_the_colors_and_before_the_widgets_and_read_back_the_same()
    {
        var content = DashboardVersions.Content("Housing", FiltersJson, Palettes.Earth, TwoColors, SurveySource, null, false, HousingWidgets());

        Assert.Equal(["name", "filters", "palette", "valueColors", "source", "widgets"], JsonDocument.Parse(content).RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(FromScript0021.Replace(""","widgets":""", $$""","source":"{{SurveySource}}","widgets":"""), content);
        var read = DashboardVersions.ReadContent(content);
        Assert.Equal(SurveySource, read.Source);
        Assert.Equal(("Housing", FiltersJson, "earth", TwoColors), (read.Name, read.Filters, read.Palette, read.ValueColors));
        Assert.Equal(["Households", "Coverage", "Water source"], read.Widgets.Select(w => w.Title).ToArray());
        Assert.Equal(content, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source, read.MinN, read.ShowN,
            read.Widgets.Select(w => Widget(w.Id.ToString(), w.Title, w.Layout, Saved, w.Config, w.ChartType))));
    }

    [Fact]
    public void A_dashboard_with_a_source_and_the_default_colors_writes_only_the_source()
    {
        var content = DashboardVersions.Content("A", "[]", Palettes.Tamiza, "[]", SurveySource, null, false, []);

        Assert.Equal($$"""{"name":"A","filters":[],"source":"{{SurveySource}}","widgets":[]}""", content);
        Assert.Equal(SurveySource, DashboardVersions.ReadContent(content).Source);
    }

    [Theory]
    [InlineData("Encuesta del año <1> & \"otra\"")]
    [InlineData("Fuente 2026 (100 %)")]
    public void A_source_with_marks_and_accents_reads_back_the_same_text(string source)
    {
        var content = DashboardVersions.Content("A", "[]", "tamiza", "[]", source, null, false, []);

        Assert.Equal(source, DashboardVersions.ReadContent(content).Source);
        Assert.Equal(source, JsonDocument.Parse(content).RootElement.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("""{"name":"A","filters":[],"widgets":[]}""")]
    [InlineData("""{"name":"A","filters":[],"source":null,"widgets":[]}""")]
    [InlineData("""{"name":"A","filters":[],"source":7,"widgets":[]}""")]
    [InlineData("""{"name":"A","filters":[],"source":["Encuesta"],"widgets":[]}""")]
    public void A_missing_or_non_text_source_reads_as_none(string json) =>
        Assert.Null(DashboardVersions.ReadContent(json).Source);

    [Fact]
    public void Saving_the_source_is_a_kind_of_change_the_history_lists_right_after_the_colors()
    {
        Assert.Equal("sourceChanged", DashboardChangeKinds.SourceChanged);
        Assert.Contains(DashboardChangeKinds.SourceChanged, DashboardChangeKinds.All);
        var kinds = DashboardChangeKinds.All.ToList();
        Assert.Equal(kinds.IndexOf(DashboardChangeKinds.ColorsChanged) + 1, kinds.IndexOf(DashboardChangeKinds.SourceChanged));
        Assert.False(DashboardChangeKinds.Closes(DashboardChangeKinds.SourceChanged));
        Assert.Equal(DashboardChangeKinds.All.Count, DashboardChangeKinds.All.Distinct().Count());
        Assert.Equal(new DashboardChange("sourceChanged"), DashboardVersions.ReadChanges("""[{"kind": "sourceChanged"}]""").Single());
    }

    [Fact]
    public void Saving_the_colors_is_a_kind_of_change_the_history_lists()
    {
        Assert.Equal("colorsChanged", DashboardChangeKinds.ColorsChanged);
        Assert.Contains(DashboardChangeKinds.ColorsChanged, DashboardChangeKinds.All);
        Assert.False(DashboardChangeKinds.Closes(DashboardChangeKinds.ColorsChanged));
        Assert.Equal(DashboardChangeKinds.All.Count, DashboardChangeKinds.All.Distinct().Count());
        Assert.Equal(new DashboardChange("colorsChanged"), DashboardVersions.ReadChanges("""[{"kind": "colorsChanged"}]""").Single());
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

    // The base settings (design D8 of add-small-sample-warnings)

    [Fact]
    public void A_dashboard_without_base_settings_has_the_content_it_had_byte_for_byte_with_or_without_a_source()
    {
        Assert.Equal(FromScript0021, DashboardVersions.Content("Housing", FiltersJson, Palettes.Earth, TwoColors, null, null, false, HousingWidgets()));
        Assert.Equal(
            FromScript0021.Replace(""","widgets":""", $$""","source":"{{SurveySource}}","widgets":"""),
            DashboardVersions.Content("Housing", FiltersJson, Palettes.Earth, TwoColors, SurveySource, null, false, HousingWidgets()));
        Assert.Equal(BeforePalettes, DashboardVersions.Content("Housing", FiltersJson, Palettes.Tamiza, "[]", null, null, false, HousingWidgets()));
    }

    [Fact]
    public void A_threshold_and_the_n_line_are_written_after_the_source_and_before_the_widgets_and_read_back_the_same()
    {
        var content = DashboardVersions.Content("Housing", FiltersJson, Palettes.Earth, TwoColors, SurveySource, 30, true, HousingWidgets());

        Assert.Equal(
            ["name", "filters", "palette", "valueColors", "source", "minN", "showN", "widgets"],
            JsonDocument.Parse(content).RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(
            FromScript0021.Replace(""","widgets":""", $$""","source":"{{SurveySource}}","minN":30,"showN":true,"widgets":"""), content);
        var read = DashboardVersions.ReadContent(content);
        Assert.Equal((30, true), (read.MinN, read.ShowN));
        Assert.Equal(content, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source, read.MinN, read.ShowN,
            read.Widgets.Select(w => Widget(w.Id.ToString(), w.Title, w.Layout, Saved, w.Config, w.ChartType))));
    }

    [Fact]
    public void Each_base_setting_is_written_alone_and_the_n_line_only_when_it_is_on()
    {
        Assert.Equal("""{"name":"A","filters":[],"minN":30,"widgets":[]}""", DashboardVersions.Content("A", "[]", "tamiza", "[]", null, 30, false, []));
        Assert.Equal("""{"name":"A","filters":[],"showN":true,"widgets":[]}""", DashboardVersions.Content("A", "[]", "tamiza", "[]", null, null, true, []));
        Assert.Equal("""{"name":"A","filters":[],"widgets":[]}""", DashboardVersions.Content("A", "[]", "tamiza", "[]", null, null, false, []));
    }

    [Theory]
    [InlineData("""{"name":"A","filters":[],"widgets":[]}""", null, false)]
    [InlineData("""{"name":"A","filters":[],"minN":null,"showN":null,"widgets":[]}""", null, false)]
    [InlineData("""{"name":"A","filters":[],"minN":"30","showN":"true","widgets":[]}""", null, false)]
    [InlineData("""{"name":"A","filters":[],"minN":0,"showN":false,"widgets":[]}""", null, false)]
    [InlineData("""{"name":"A","filters":[],"minN":10001,"widgets":[]}""", null, false)]
    [InlineData("""{"name":"A","filters":[],"minN":2.5,"widgets":[]}""", null, false)]
    [InlineData("""{"name":"A","filters":[],"minN":1,"showN":true,"widgets":[]}""", 1, true)]
    [InlineData("""{"name":"A","filters":[],"minN":10000,"widgets":[]}""", 10000, false)]
    public void A_missing_or_out_of_range_base_setting_reads_as_off(string json, int? minN, bool showN)
    {
        var read = DashboardVersions.ReadContent(json);

        Assert.Equal((minN, showN), (read.MinN, read.ShowN));
    }

    [Fact]
    public void A_content_recorded_before_base_settings_reads_them_off_and_builds_the_same_content_again()
    {
        var read = DashboardVersions.ReadContent(FromScript0021);

        Assert.Equal((null, false), (read.MinN, read.ShowN));
        Assert.Equal(FromScript0021, DashboardVersions.Content(read.Name, read.Filters, read.Palette, read.ValueColors, read.Source, read.MinN, read.ShowN,
            read.Widgets.Select(w => Widget(w.Id.ToString(), w.Title, w.Layout, Saved, w.Config, w.ChartType))));
    }

    [Fact]
    public void Saving_the_base_settings_is_a_kind_of_change_the_history_lists_right_after_the_source()
    {
        Assert.Equal("basesChanged", DashboardChangeKinds.BasesChanged);
        var kinds = DashboardChangeKinds.All.ToList();
        Assert.Equal(kinds.IndexOf(DashboardChangeKinds.SourceChanged) + 1, kinds.IndexOf(DashboardChangeKinds.BasesChanged));
        Assert.False(DashboardChangeKinds.Closes(DashboardChangeKinds.BasesChanged));
    }
}
