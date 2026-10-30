using System.Text.Json;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.UnitTests.Mcp;

public sealed class McpResultBudgetTests
{
    private static object Tables(int tables, int columnsEach) => new
    {
        project = "Water survey",
        tables = Enumerable.Range(0, tables).Select(t => new
        {
            name = $"table_{t}",
            columns = Enumerable.Range(0, columnsEach).Select(c => new { name = $"column_{t}_{c}", type = "text", label = $"Question {c} of table {t}" }).ToList(),
        }).ToList(),
    };

    [Fact]
    public void A_result_under_the_size_is_unchanged()
    {
        var value = Tables(2, 3);
        var expected = JsonSerializer.Serialize(value, McpResultBudget.Json);

        Assert.Equal(expected, McpResultBudget.Serialize(value, 60_000, "columns"));
    }

    [Fact]
    public void Nulls_and_enums_are_written_the_way_the_rest_api_writes_them()
    {
        var text = McpResultBudget.Serialize(new { Name = "x", Missing = (string?)null, Level = Tamiza.Api.Data.McpAccess.Aggregates }, 60_000);

        Assert.Equal("""{"name":"x","level":"aggregates"}""", text);
    }

    [Fact]
    public void A_result_over_the_size_loses_items_from_the_end_of_its_longest_list_and_says_so()
    {
        var text = McpResultBudget.Serialize(Tables(1, 400), 6_000, "columns");

        Assert.True(text.Length <= 6_000, $"{text.Length} characters");
        using var body = JsonDocument.Parse(text);
        var columns = body.RootElement.GetProperty("tables")[0].GetProperty("columns");
        var kept = columns.GetArrayLength();
        Assert.InRange(kept, 1, 399);
        var note = body.RootElement.GetProperty("truncated");
        Assert.Equal("tables[0].columns", note.GetProperty("list").GetString());
        Assert.Equal(400 - kept, note.GetProperty("omitted").GetInt32());
        Assert.Equal("columns", note.GetProperty("hint").GetString());
        // The first items are the ones kept.
        Assert.Equal("column_0_0", columns[0].GetProperty("name").GetString());
        Assert.Equal($"column_0_{kept - 1}", columns[kept - 1].GetProperty("name").GetString());
    }

    [Fact]
    public void The_longest_list_is_cut_again_until_it_fits_and_others_are_named_when_they_were_cut_too()
    {
        var text = McpResultBudget.Serialize(Tables(40, 60), 4_000, "columns");

        Assert.True(text.Length <= 4_000, $"{text.Length} characters");
        using var body = JsonDocument.Parse(text);
        var note = body.RootElement.GetProperty("truncated");
        Assert.True(note.GetProperty("omitted").GetInt32() > 0);
        Assert.Equal("Water survey", body.RootElement.GetProperty("project").GetString());
    }

    [Fact]
    public void A_result_that_fits_after_one_cut_says_nothing_about_others()
    {
        var text = McpResultBudget.Serialize(Tables(1, 400), 6_000);

        using var body = JsonDocument.Parse(text);
        Assert.False(body.RootElement.GetProperty("truncated").TryGetProperty("others", out _));
    }

    [Fact]
    public void The_size_setting_is_honored()
    {
        var value = Tables(1, 200);

        var small = McpResultBudget.Serialize(value, 5_000);
        var large = McpResultBudget.Serialize(value, 60_000);

        Assert.True(small.Length <= 5_000);
        Assert.Contains("truncated", small);
        Assert.DoesNotContain("truncated", large);
    }

    [Fact]
    public void A_long_text_with_no_list_to_cut_loses_its_end()
    {
        var text = McpResultBudget.Serialize(new { name = "indicator", code = new string('x', 20_000) }, 5_000);

        Assert.True(text.Length <= 5_000, $"{text.Length} characters");
        using var body = JsonDocument.Parse(text);
        var note = body.RootElement.GetProperty("truncated");
        Assert.Equal("code", note.GetProperty("list").GetString());
        Assert.Equal("characters", note.GetProperty("unit").GetString());
        Assert.True(note.GetProperty("omitted").GetInt32() > 10_000);
        Assert.StartsWith("xxxx", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void A_non_object_result_is_left_as_it_is()
    {
        var text = McpResultBudget.Serialize(Enumerable.Range(0, 5_000).ToList(), 1_000);

        Assert.True(text.Length > 1_000);
        Assert.StartsWith("[0,1,2", text);
    }
}
