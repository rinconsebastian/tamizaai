using Tamiza.DbUp;

namespace Tamiza.Api.UnitTests.Database;

/// <summary>DbUp applies scripts by name, once; these rules keep that order predictable.</summary>
public sealed class ScriptConventionTests
{
    private static readonly string[] Resources = typeof(SchemaMigrator).Assembly.GetManifestResourceNames();

    [Fact]
    public void Every_embedded_resource_is_a_numbered_sql_script()
    {
        Assert.NotEmpty(Resources);
        Assert.All(Resources, name => Assert.Matches(@"^\d{4}_[a-z0-9_]+\.sql$", name));
    }

    [Fact]
    public void Sequence_numbers_are_unique()
    {
        var duplicates = Resources
            .GroupBy(name => name.Split('_')[0])
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group));

        Assert.Empty(duplicates);
    }
}
