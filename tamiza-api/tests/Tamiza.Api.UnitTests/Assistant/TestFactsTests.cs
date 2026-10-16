using System.Text.Json;
using Tamiza.Api.Assistant;
using Tamiza.Api.Data;
using static Tamiza.Api.UnitTests.Assistant.ContextFixtures;

namespace Tamiza.Api.UnitTests.Assistant;

public sealed class TestFactsTests
{
    private const string Draft = IndicatorDraft;

    /// <summary>The <c>&lt;test&gt;</c> block of a fix whose draft step is <paramref name="step"/>, after a flatten and a clean step that worked.</summary>
    private static string TestBlock(
        TestStep step, string draft = Draft, AssistantWarning? warning = null, IEnumerable<TestStep>? before = null,
        ScriptKind kind = ScriptKind.Indicator, ContextSources? sources = null)
    {
        var steps = before ?? [Step("flatten", tables: [Table("main", 500, "_id", "municipality", "water_source")]), Step("clean", tables: [Table("main", 498, "_id", "municipality", "water_source")])];
        var test = Test(kind, draft, [.. steps, step]);
        var name = kind == ScriptKind.Indicator ? "indicator" : kind.ToString().ToLowerInvariant();
        var request = Request(name, "fix", draft, test: test, warning: warning, messages: [new AssistantMessage("user", "fix it")]);
        return Block(ContextAssembler.Assemble(sources ?? Sources(name), request, 1_000_000), "test");
    }

    private static string Error(string error, string draft = Draft, ScriptKind kind = ScriptKind.Indicator) =>
        TestBlock(Step("indicator", "failed", error: error), draft);

    [Fact]
    public void KnownColumnKept()
    {
        var block = Error(Traceback("KeyError: 'water_source'", (12, "compute", "main[\"water_source\"]")));

        Assert.Contains("  error: KeyError: 'water_source'\n", block);
    }

    [Fact]
    public void QuotedValueMasked()
    {
        var block = Error(Traceback("ValueError: could not convert string to float: '3001234567'", (12, "compute", "float(x)")));

        Assert.Contains("  error: ValueError: could not convert string to float: '…'\n", block);
        Assert.DoesNotContain("3001234567", block);
    }

    [Fact]
    public void UnquotedValueMasked()
    {
        var block = Error(Traceback(
            "pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: Juan Perez", (12, "compute", "pd.to_datetime(x)")));

        Assert.Contains("  error: pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: …\n", block);
        Assert.DoesNotContain("Juan", block);
        Assert.DoesNotContain("Perez", block);
    }

    [Fact]
    public void MessageTheScriptWrote()
    {
        var draft = Draft + "    raise ValueError(f\"only {n} rows, first {name}\")\n";

        var block = Error(Traceback("ValueError: only 3 rows, first Ana Gómez", (14, "compute", "raise ValueError(...)")), draft);

        Assert.Contains("  error: ValueError: only 3 rows, first …\n", block);
    }

    [Fact]
    public void ColumnMadeFromData_IsMaskedWhereverTheBlockNamesIt()
    {
        var view = new IndicatorTestView(
            ["juan_perez"], "juan_perez", false, ["municipality"], "municipality", [], 40, 4, 1.0, new() { ["juan_perez"] = 12.5 }, 0,
            new() { ["municipality"] = new DimensionChart(12, null) });
        var table = new TestTable("piped_water_rate", 40, [new("municipality", "text"), new("juan_perez", "decimal")], 0, null, null);
        var shown = new ShownPreview("juan_perez", 20, 40, new TestTable("pivot", 40, [new("juan_perez", "decimal")], 0, null, null), null);

        var block = TestBlock(
            Step("indicator", tables: [table], shown: [shown], dashboard: view),
            warning: new AssistantWarning("emptyValues", Measure: "juan_perez", Count: 4));

        Assert.DoesNotContain("juan", block);
        Assert.DoesNotContain("perez", block);
        Assert.Contains("columns: municipality (text), … (decimal)", block);
        Assert.Contains("- measures: … (default: …)", block);
        Assert.Contains("ctx.show \"…\" at line 20: 40 rows; columns: … (decimal)", block);
        Assert.Contains("4 rows have no value in the default measure …", block);
    }

    [Fact]
    public void ANameThatOnlyTheTestHoldsNeverAllowsItself()
    {
        var table = Table("zona_norte", 12, "zona_norte", "municipality");
        var shown = new ShownPreview("zona_norte", 9, 12, table, null);

        var masked = TestBlock(Step("output", tables: [table], shown: [shown]), kind: ScriptKind.Output);

        Assert.DoesNotContain("zona", masked);
        Assert.DoesNotContain("norte", masked);
        Assert.Contains("table …: 12 rows; columns: … (text), municipality (text)", masked);

        // The same name is kept when the draft the request sends holds it.
        var kept = TestBlock(Step("output", tables: [table], shown: [shown]), draft: "return {\"zona_norte\": df}", kind: ScriptKind.Output);
        Assert.Contains("table zona_norte: 12 rows", kept);
    }

    [Fact]
    public void NameSplitAcrossLabels()
    {
        // The draft compares with both labels of the municipalities list.
        var draft = "main[main[\"municipality\"].isin([\"Santa Ana\", \"Gómez Plata\"])]";

        var block = Error(Traceback("ValueError: first Ana Gómez", (3, "compute", "x")), draft);

        Assert.Contains("  error: ValueError: first …\n", block);
    }

    [Fact]
    public void WholeLabelKept()
    {
        var block = Error(Traceback("ValueError: invalid literal for int() with base 10: 'Santa Ana'", (3, "compute", "int(x)")));

        Assert.Contains("  error: ValueError: invalid literal for int() with base 10: 'Santa Ana'\n", block);
    }

    [Fact]
    public void SeparatedNumbers()
    {
        var log = string.Join('\n',
            "INFO phone 1.023.456.789",
            "INFO phone 300 123 4567",
            "INFO phone 300 - 123 - 4567",
            "INFO date 1985-03-12",
            "INFO place 4.6097, -74.0817",
            "INFO month 2026-09 total 4,520");
        var table = Table("piped_water_rate", 3, "municipality", "c_300_123_4567");

        var block = TestBlock(Step("indicator", tables: [table], log: log), draft: Draft + "    ctx.log.info(\"phone date place month total\")\n");

        Assert.Contains("    INFO phone …\n    INFO phone …\n    INFO phone …\n    INFO date …\n    INFO place …\n", block);
        Assert.Contains("    INFO month 2026-09 total 4,520", block);
        Assert.Contains("columns: municipality (text), … (text)", block);
    }

    [Fact]
    public void ExceptionTypeKept()
    {
        var block = Error(Traceback(
            "pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: Juan Perez", (9, "compute", "x")));

        Assert.Contains("pandas._libs.tslibs.parsing.DateParseError:", block);
    }

    [Fact]
    public void MessageOnSeveralLines()
    {
        var draft = Draft + "    raise ValueError(f\"Duplicated submissions:\\n{names}\")\n";

        var block = Error(Traceback("ValueError: Duplicated submissions:\njuan.perez: 3", (15, "compute", "raise ValueError(...)")), draft);

        Assert.Contains("  error: ValueError: Duplicated submissions:\n", block);
        Assert.DoesNotContain("juan", block);
        Assert.DoesNotContain("perez", block);
    }

    [Fact]
    public void LibraryHintAfterTheMessage()
    {
        var draft = Draft + "    pd.to_datetime(main[\"visit_date\"], format=\"%Y-%m-%d\")\n";
        const string error = """
            - passing `format='mixed'`, and the format will be inferred for each element individually. You might want to use `dayfirst` alongside this.

            Traceback (most recent call last):
              File "script.py", line 14, in compute
                pd.to_datetime(main["visit_date"], format="%Y-%m-%d")
                ^^^^^
            ValueError: time data "Juan Perez" doesn't match format "%Y-%m-%d", at position 0. You might want to try:
                - passing `format` if your strings have a consistent format;
                - passing `format='ISO8601'` if your strings are all ISO8601 but not necessarily in exactly the same format;
                - passing `format='mixed'`, and the format will be inferred for each element individually. You might want to use `dayfirst` alongside this.
            """;

        var block = Error(error, draft);

        Assert.Contains("  error: ValueError: time data \"…\" doesn't match format \"%Y-%m-%d\"", block);
        Assert.DoesNotContain("mixed", block);
        Assert.DoesNotContain("ISO8601", block);
        Assert.DoesNotContain("Juan", block);
    }

    [Fact]
    public void Traceback_SendsTheLineAndFunctionOfEachFrameWithoutTheSource()
    {
        var error = Traceback("KeyError: 'water_source'", (5, "rate", "return df[\"water_source\"]"), (12, "compute", "rate(main)"));

        var block = Error(error);

        Assert.EndsWith("  error: KeyError: 'water_source'\n    line 5 in rate\n    line 12 in compute", block);
        Assert.DoesNotContain("return df", block);
        Assert.DoesNotContain("rate(main)", block);
        Assert.DoesNotContain("^^^^", block);
    }

    [Fact]
    public void SyntaxError()
    {
        const string error = """
            SyntaxError: invalid syntax

              File "script.py", line 7
                x = = 3
                    ^
            SyntaxError: invalid syntax
            """;

        var block = Error(error);

        Assert.EndsWith("  error: SyntaxError: invalid syntax\n    line 7", block);
        Assert.DoesNotContain("x = = 3", block);
    }

    [Fact]
    public void AnExceptionWithANoteSendsNoNote()
    {
        const string error = """
            This note names Juan Perez

            Traceback (most recent call last):
              File "script.py", line 5, in compute
                main["age"].astype(int)
            ValueError: cannot convert float NaN to integer
            This note names Juan Perez
            """;

        var block = Error(error);

        Assert.Contains("  error: ValueError: cannot convert float NaN to integer\n    line 5 in compute", block);
        Assert.DoesNotContain("note", block);
        Assert.DoesNotContain("Juan", block);
    }

    [Fact]
    public void ADuckDbConversionErrorEndingInACaretSendsTheTypeAndTheFirstMessageLine()
    {
        const string error = """
                                           ^

            Traceback (most recent call last):
              File "script.py", line 8, in compute
                duckdb.sql("SELECT CAST('Juan Perez' AS INTEGER)")
            _duckdb.ConversionException: Conversion Error: Could not convert string 'Juan Perez' to INT32
            LINE 1: SELECT CAST('Juan Perez' AS INTEGER)
                                ^
            """;

        var block = Error(error);

        Assert.Contains("  error: _duckdb.ConversionException: Conversion Error: Could not convert string '…' to …\n    line 8 in compute", block);
        Assert.DoesNotContain("LINE 1", block);
        Assert.DoesNotContain("SELECT", block);
        Assert.DoesNotContain("Juan", block);
    }

    [Fact]
    public void AMessageWithoutATracebackIsMaskedAsAWhole()
    {
        var block = Error("The script called exit(Duplicate Juan Perez).");

        Assert.EndsWith("  error: The script called exit(Duplicate …).", block);
    }

    [Theory]
    [InlineData("KeyError: 'Juan Perez'", "KeyError: '…'")]
    [InlineData("ValueError: invalid literal for int() with base 10: '3001234567'", "ValueError: invalid literal for int() with base 10: '…'")]
    [InlineData("ValueError: Unable to parse string \"Juan Perez\" at position 0", "ValueError: Unable to parse string \"…\" at position 0")]
    [InlineData("tamiza_sdk.context.ReferenceTableNotFound: There is no reference table 'poblacion_2020'. Available: population.", "tamiza_sdk.context.ReferenceTableNotFound: There is no reference table '…'. Available: population.")]
    public void MessagesObservedWhilePlanning(string exception, string expected)
    {
        var block = Error(Traceback(exception, (9, "compute", "main.loc[x]")));

        Assert.Contains($"  error: {expected}\n", block);
    }

    [Fact]
    public void AReferenceTableTheDraftNamesIsKept()
    {
        var draft = Draft + "    ctx.reference[\"poblacion\"]\n";

        var block = Error(Traceback("tamiza_sdk.context.ReferenceTableNotFound: There is no reference table 'poblacion'. Available: population.", (9, "compute", "x")), draft);

        Assert.Contains("There is no reference table 'poblacion'. Available: population.", block);
    }

    [Fact]
    public void ExitWithACode()
    {
        Assert.EndsWith("  error: The script called exit(1).", Error("The script called exit(1)."));
    }

    [Fact]
    public void AWorkerFailureSendsItsErrorMaskedAndNoneOfItsTraceback()
    {
        const string log = """
            INFO Table indicator: column 'Juan Perez' published as 'juan_perez'.
            Traceback (most recent call last):
              File "/app/tamiza/pipeline.py", line 431, in _run_step
                action(record)
            psycopg.errors.OperationalError: connection to server at "192.168.100.200", port 5432 failed: Juan Perez
            Duration: 0.4 s
            """;

        var block = TestBlock(Step("indicator", "failed", log: log, error: "OperationalError: connection to server at \"192.168.100.200\", port 5432 failed: Juan Perez"));

        Assert.Contains("  error: OperationalError: connection to server at \"…\", port 5432 failed: …\n", block);
        Assert.Contains("    INFO Table indicator: column '…' published as '…'.", block);
        Assert.DoesNotContain("pipeline.py", block);
        Assert.DoesNotContain("psycopg", block);
        Assert.DoesNotContain("Duration", block);
        Assert.DoesNotContain("192.168", block);
    }

    [Fact]
    public void LogLines()
    {
        var draft = Draft + "    ctx.log.info(\"Dropped %d test submissions of %s\", n, name)\n";
        const string log = "INFO Dropped 3 test submissions of Ana Gómez\nprinted line Juan Perez\nWARNING Dropped 1 test submissions of Juan Perez";

        var block = TestBlock(Step("indicator", log: log), draft);

        Assert.Contains("    INFO Dropped 3 test submissions of …\n    WARNING Dropped 1 test submissions of …", block);
        Assert.DoesNotContain("printed", block);
    }

    [Fact]
    public void LongLog_OnlyTheLastSixtyLinesWithALevelAreSent()
    {
        var log = string.Join('\n', Enumerable.Range(1, 400).Select(n => $"INFO Dropped {n} test submissions"));

        var block = TestBlock(Step("indicator", log: log));

        Assert.Equal(60, block.Split('\n').Count(line => line.StartsWith("    INFO Dropped ", StringComparison.Ordinal)));
        Assert.Contains("    INFO Dropped 341 test submissions\n", block);
        Assert.Contains("    INFO Dropped 400 test submissions", block);
        Assert.DoesNotContain("Dropped 340 ", block);
    }

    [Fact]
    public void PrintedOutput_IsNeverSent()
    {
        const string log = "INFO Dropped 3 test submissions\n   _id municipality\n0  1001 Santa Ana\n1  1002 Gómez Plata\nINFO: not a log line of ctx.log\nINFOX Dropped";

        var block = TestBlock(Step("indicator", log: log));

        Assert.Contains("    INFO Dropped 3 test submissions", block);
        Assert.DoesNotContain("1001", block);
        Assert.DoesNotContain("not a log line", block);
        Assert.DoesNotContain("INFOX", block);
        Assert.Equal(1, block.Split('\n').Count(line => line.StartsWith("    INFO", StringComparison.Ordinal)));
    }

    [Fact]
    public void AVeryLongLogLineIsCut()
    {
        var log = "INFO " + string.Join(' ', Enumerable.Repeat("Dropped", 500));

        var block = TestBlock(Step("indicator", log: log));

        var line = block.Split('\n').Single(l => l.StartsWith("    INFO ", StringComparison.Ordinal));
        Assert.True(line.Length < TestFacts.MaxLineChars + 20);
        Assert.EndsWith("…", line);
    }

    [Fact]
    public void PreviewRows_OnlyNamesColumnsTypesAndCountsAreSent()
    {
        var preview = JsonSerializer.Deserialize<List<List<JsonElement>>>("""[["SENTINEL_PREVIEW_VALUE", 1001, "Santa Ana"]]""")!;
        var table = new TestTable("main", 500, [new("_id", "integer"), new("municipality", "text")], 1, preview, [[0, 1]]);
        var shownTable = new TestTable("rate", 12, [new("municipality", "text"), new("value", "decimal")], 1, preview, [[0, 0]]);
        var shown = new List<ShownPreview> { new("rate", 20, 12, shownTable, null), new("note", 31, 1, null, "SENTINEL_TEXT_VALUE") };
        var draft = Draft + "    ctx.show(rate, \"rate\")\n    ctx.show(note, \"note\")\n";

        var block = TestBlock(Step("indicator", "failed", tables: [table], shown: shown, error: "The script failed."), draft);

        Assert.Contains("- step indicator (the draft): failed", block);
        Assert.Contains("  - table main: 500 rows; columns: _id (integer), municipality (text)", block);
        Assert.Contains("  - ctx.show \"rate\" at line 20: 12 rows; columns: municipality (text), value (decimal)", block);
        Assert.Contains("  - ctx.show \"note\" at line 31: a text value", block);
        Assert.DoesNotContain("SENTINEL", block);
        Assert.DoesNotContain("1001", block);
    }

    [Fact]
    public void ChartGroups_OnlyTheNumberOfGroupsIsSent()
    {
        var view = new IndicatorTestView(
            ["value", "numerator", "denominator"], "value", true, ["enumerator", "municipality"], "enumerator", [], 24, 0, 87.5,
            new() { ["value"] = 987654.321 }, 0,
            new()
            {
                ["enumerator"] = new DimensionChart(12, [new ChartGroup("SENTINEL_GROUP", 42.25, 11.5, 13.5)]),
                ["municipality"] = new DimensionChart(3, [new ChartGroup("Santa Ana", 1.5, 1.5, 2.5)]),
            });

        var block = TestBlock(Step("indicator", dashboard: view), draft: Draft + "    main.groupby(\"enumerator\")\n");

        Assert.Contains("- measures: value, numerator, denominator (default: value)", block);
        Assert.Contains("- ratio of numerator to denominator: yes", block);
        Assert.Contains("- dimensions: enumerator, municipality (default: enumerator)", block);
        Assert.Contains("- rows: 24", block);
        Assert.Contains("- distinct values per dimension: enumerator 12, municipality 3", block);
        foreach (var value in new[] { "SENTINEL_GROUP", "987654", "87.5", "42.25", "11.5", "13.5", "Santa Ana" })
        {
            Assert.DoesNotContain(value, block);
        }
    }

    [Fact]
    public void FailedIndicator_HasThreeStepsAndOnlyTheDraftsErrorAndLog()
    {
        var steps = new[]
        {
            Step("flatten", tables: [Table("main", 500, "_id", "municipality", "water_source"), Table("members", 1500, "_row_id", "age")], log: "INFO Flattened 500 submissions into 2 tables."),
            Step("clean", tables: [Table("main", 498, "_id", "municipality", "water_source")], log: "INFO SENTINEL_CLEAN_LOG", error: "SENTINEL_CLEAN_ERROR"),
        };

        var block = TestBlock(
            Step("indicator", "failed", error: Traceback("KeyError: 'water_source'", (12, "compute", "x")), log: "INFO Dropped 2 test submissions"),
            before: steps);

        Assert.Contains("- step flatten: succeeded\n  - table main: 500 rows; columns: _id (text), municipality (text), water_source (text)\n  - table members: 1500 rows; columns: _row_id (text), age (text)\n", block);
        Assert.Contains("- step clean (the saved script 1): succeeded\n  - table main: 498 rows", block);
        Assert.Contains("- step indicator (the draft): failed\n  error: KeyError: 'water_source'\n    line 12 in compute\n", block);
        Assert.DoesNotContain("SENTINEL", block);
        Assert.DoesNotContain("Flattened", block);
        Assert.Contains("    INFO Dropped 2 test submissions", block);
    }

    [Fact]
    public void AStoppedStepNamesItsLine()
    {
        var block = TestBlock(Step("indicator", "stopped", stoppedAt: 14));

        Assert.Contains("- step indicator (the draft): stopped, stopped at line 14", block);
    }

    [Fact]
    public void WarningAskedAbout()
    {
        var view = new IndicatorTestView(
            ["value", "year"], "value", false, ["municipality"], "municipality", [], 24, 0, 0.5, new() { ["year"] = 48600 }, 0, []);
        var table = new TestTable("piped_water_rate", 24, [new("municipality", "text"), new("year", "integer"), new("value", "decimal")], 0, null, null);

        var block = TestBlock(Step("indicator", tables: [table], dashboard: view), draft: Draft + "    rate[\"year\"] = 2026\n", warning: new AssistantWarning("dimensionAsMeasure", Column: "year"));

        Assert.Contains("(dimensionAsMeasure): the numeric column year looks like a dimension and is offered as a measure.", block);
    }

    [Fact]
    public void AWarningNameThatIsNotAColumnOfTheTestIsDropped()
    {
        var table = new TestTable("piped_water_rate", 24, [new("municipality", "text"), new("value", "decimal")], 0, null, null);

        var block = TestBlock(Step("indicator", tables: [table]), warning: new AssistantWarning("noValue", Measure: "juan_perez"));

        Assert.DoesNotContain("juan", block);
        Assert.Contains("(noValue): the table has no column named value, so the dashboard plots another numeric column.", block);
    }

    [Fact]
    public void PercentWarning_SaysValuesAreAboveOneAndNotTheLargest()
    {
        var warning = AssistantWarning.Create("percentAboveOne", Params("""{"max": 87.5, "shown": "8,750%", "measure": "value"}"""))!;
        var view = new IndicatorTestView(["value"], "value", false, ["municipality"], "municipality", [], 24, 0, 87.5, new() { ["value"] = 87.5 }, 0, []);

        var block = TestBlock(Step("indicator", dashboard: view), warning: warning);

        Assert.Contains("(percentAboveOne): the indicator's format is percent and values of the default measure are above 1.", block);
        Assert.DoesNotContain("87.5", block);
        Assert.DoesNotContain("8,750", block);
        Assert.Equal(new AssistantWarning("percentAboveOne"), warning);
    }

    [Theory]
    [InlineData("noValue", """{"measure": "value", "count": 3, "column": "x", "max": 1}""", "value", null, null)]
    [InlineData("emptyValues", """{"measure": "value", "count": 4, "column": "x"}""", "value", null, 4L)]
    [InlineData("dimensionAsMeasure", """{"column": "year", "measure": "value", "count": 4}""", null, "year", null)]
    [InlineData("duplicateRows", """{"count": 12, "measure": "value"}""", null, null, 12L)]
    [InlineData("noRows", """{"count": 12, "measure": "value"}""", null, null, null)]
    [InlineData("ratioIncomplete", """{"count": 12}""", null, null, null)]
    [InlineData("emptyValues", """{"measure": 12, "count": "4"}""", null, null, null)]
    [InlineData("emptyValues", """{"measure": "value", "count": 4.5}""", "value", null, null)]
    [InlineData("emptyValues", """{"measure": "", "count": -1}""", null, null, null)]
    public void WarningParamsAreKeptPerKey(string key, string json, string? measure, string? column, long? count)
    {
        var warning = AssistantWarning.Create(key, Params(json));

        Assert.Equal(new AssistantWarning(key, measure, column, count), warning);
    }

    [Fact]
    public void AnUnknownWarningKeyHasNoWarning()
    {
        Assert.Null(AssistantWarning.Create("somethingElse", Params("{}")));
        Assert.Null(AssistantWarning.Create(null, null));
        Assert.Equal(new AssistantWarning("duplicateRows"), AssistantWarning.Create("duplicateRows", null));
        Assert.Equal(7, AssistantWarning.Keys.Count);
    }

    [Fact]
    public void AVeryLongWarningNameIsDropped()
    {
        var warning = AssistantWarning.Create("noValue", Params($$"""{"measure": "{{new string('a', AssistantWarning.MaxNameLength + 1)}}"}"""));

        Assert.Equal(new AssistantWarning("noValue"), warning);
    }

    [Fact]
    public void ATestWithoutStepsSaysSo()
    {
        var test = Test(ScriptKind.Indicator, Draft, []);
        var request = Request(action: "fix", test: test);

        var block = Block(ContextAssembler.Assemble(Sources(), request, 1_000_000), "test");

        Assert.Contains("No step was recorded.", block);
    }

    [Fact]
    public void TheTestStatusAndSampleAreStated()
    {
        var block = TestBlock(Step("indicator", "failed"));

        Assert.StartsWith("The analyst's last test of this script: the indicator script piped_water_rate on a sample of 500 submissions; the test failed.", block);
    }

    [Fact]
    public void ParseError_FindsTheExceptionAfterTheFrames()
    {
        var parsed = TestFacts.ParseError(Traceback("KeyError: 'x'", (5, "rate", "a"), (12, "compute", "b")));

        Assert.True(parsed.HasTraceback);
        Assert.Equal("KeyError: 'x'", parsed.Exception);
        Assert.Equal(new[] { new ErrorFrame(5, "rate"), new ErrorFrame(12, "compute") }, parsed.Frames);
    }

    [Fact]
    public void ParseError_ATracebackWithoutScriptFramesStillHasItsException()
    {
        var parsed = TestFacts.ParseError("ValueError: x\n\nTraceback (most recent call last):\nValueError: x");

        Assert.True(parsed.HasTraceback);
        Assert.Equal("ValueError: x", parsed.Exception);
        Assert.Empty(parsed.Frames);
    }

    [Fact]
    public void ParseError_APlainMessageHasNoTraceback()
    {
        var parsed = TestFacts.ParseError("The script was stopped because it exceeded the time limit.");

        Assert.False(parsed.HasTraceback);
        Assert.Equal("The script was stopped because it exceeded the time limit.", parsed.Message);
    }

    [Fact]
    public void ParseError_FramesOfOtherFilesAreNotScriptFrames()
    {
        var parsed = TestFacts.ParseError("E: x\n\nTraceback (most recent call last):\n  File \"/app/tamiza/runner.py\", line 63, in run_step\n    function()\nE: x");

        Assert.Empty(parsed.Frames);
        Assert.Equal("E: x", parsed.Exception);
    }

    [Fact]
    public void LogLines_AreTheLastSixtyThatStartWithALevel()
    {
        var log = string.Join("\r\n", Enumerable.Range(1, 100).Select(n => n % 2 == 0 ? $"ERROR e{n}" : $"print {n}"));

        var lines = TestFacts.LogLines(log);

        Assert.Equal(50, lines.Count);
        Assert.Equal(("ERROR", "e2"), lines[0]);
        Assert.Empty(TestFacts.LogLines(null));
    }

    [Fact]
    public void NoValueOfThePreviewTotalsOrGroupsReachesTheRequestWhenEverythingIsFilled()
    {
        var preview = JsonSerializer.Deserialize<List<List<JsonElement>>>("""[["SENTINEL_ROW", 31415926]]""")!;
        var table = new TestTable("piped_water_rate", 24, [new("municipality", "text"), new("value", "decimal")], 1, preview, [[0, 0]]);
        var view = new IndicatorTestView(
            ["value"], "value", false, ["municipality"], "municipality", [], 24, 2, 87.5, new() { ["value"] = 27182818.28 }, 1,
            new() { ["municipality"] = new DimensionChart(3, [new ChartGroup("SENTINEL_GROUP", 1618033.98, null, null)]) });
        var shown = new ShownPreview("rate", 20, 24, table, "SENTINEL_TEXT");

        var context = ContextAssembler.Assemble(
            Sources(),
            Request(action: "fix", test: Test(ScriptKind.Indicator, Draft, [Step("indicator", tables: [table], shown: [shown], dashboard: view)])),
            1_000_000);

        foreach (var value in new[] { "SENTINEL", "31415926", "27182818", "1618033", "87.5" })
        {
            Assert.DoesNotContain(value, context.System);
        }
    }
}
