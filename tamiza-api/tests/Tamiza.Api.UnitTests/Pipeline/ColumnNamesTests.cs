using System.Text.Json;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.UnitTests.Pipeline;

/// <summary>The API's port of the naming rule against the cases the analytics tests also check.</summary>
public sealed class ColumnNamesTests
{
    private static readonly JsonElement Shared = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pipeline", "column_names.json"))).RootElement;

    public static TheoryData<string, string> ReduceCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var item in Shared.GetProperty("reduce").EnumerateArray())
        {
            data.Add(item.GetProperty("name").GetString()!, item.GetProperty("reduced").GetString()!);
        }

        return data;
    }

    public static TheoryData<string[], string[]> DeduplicateCases()
    {
        var data = new TheoryData<string[], string[]>();
        foreach (var item in Shared.GetProperty("deduplicate").EnumerateArray())
        {
            data.Add(Strings(item.GetProperty("names")), Strings(item.GetProperty("deduplicated")));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReduceCases))]
    public void Reduces_like_the_analytics_worker(string name, string reduced) => Assert.Equal(reduced, ColumnNames.Reduce(name));

    [Theory]
    [MemberData(nameof(DeduplicateCases))]
    public void Deduplicates_like_the_analytics_worker(string[] names, string[] deduplicated) =>
        Assert.Equal(deduplicated, ColumnNames.Deduplicate(names));

    [Fact]
    public void The_shared_file_has_cases() => Assert.True(ReduceCases().Count > 20);

    [Theory]
    [InlineData("population", true)]
    [InlineData("persons_2026", true)]
    [InlineData("a", true)]
    [InlineData("Population 2026", false)]
    [InlineData("2026", false)]
    [InlineData("_private", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_table_names(string? name, bool valid) => Assert.Equal(valid, ColumnNames.IsValidTableName(name));

    [Fact]
    public void Rejects_table_names_over_63_characters() => Assert.False(ColumnNames.IsValidTableName(new string('a', 64)));

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}
