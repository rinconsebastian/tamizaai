using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tamiza.Api.UnitTests.Assistant;

/// <summary>
/// The comparisons behind the prompt kit's drift tests, over plain text so that a test can also feed them a changed copy and
/// check that they notice.
/// </summary>
internal static partial class PromptKitChecks
{
    /// <summary>
    /// Finds a file or folder of the repository by walking up from the test assembly. The sibling projects sit next to the API;
    /// a build that ships only the API has nothing to compare with, and the tests that need them then do nothing.
    /// </summary>
    public static string? FindUp(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path) || Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public const string TemplatesFolder = "tamiza-ui/src/app/pages/projects/script-templates";

    /// <summary>Line endings do not matter to a template: git may check files out with either.</summary>
    public static bool SameText(string a, string b) => Normalize(a) == Normalize(b);

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>The exact versions of <c>[project] dependencies</c> in pyproject.toml, without the database driver scripts never see.</summary>
    public static Dictionary<string, string> PinnedLibraries(string pyproject)
    {
        var block = DependenciesBlock().Match(pyproject);
        Assert.True(block.Success, "pyproject.toml has no [project] dependencies list.");
        return Pin().Matches(block.Groups[1].Value)
            .Select(match => (Name: match.Groups[1].Value.ToLowerInvariant(), Version: match.Groups[2].Value))
            .Where(pin => pin.Name != "psycopg")
            .ToDictionary(pin => pin.Name, pin => pin.Version);
    }

    /// <summary>The version uv.lock locks for a package, or null.</summary>
    public static string? LockedVersion(string uvLock, string package)
    {
        var match = Regex.Match(uvLock, "name = \"" + Regex.Escape(package) + "\"\\s*\\nversion = \"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The libraries named with a version in the "Libraries available:" line of system.md.</summary>
    public static Dictionary<string, string> LibraryLine(string systemMd)
    {
        var line = systemMd.Split('\n').SingleOrDefault(l => l.StartsWith("Libraries available:", StringComparison.Ordinal));
        Assert.True(line is not null, "system.md has no single line that starts with 'Libraries available:'.");
        return Versioned().Matches(line!).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
    }

    /// <summary>What differs between the library line and the pins; empty when they agree.</summary>
    public static List<string> LibraryMismatches(string systemMd, string pyproject, string uvLock)
    {
        var expected = PinnedLibraries(pyproject);
        var shapely = LockedVersion(uvLock, "shapely");
        Assert.True(shapely is not null, "uv.lock has no shapely.");
        expected["shapely"] = shapely!;

        var actual = LibraryLine(systemMd);
        var problems = new List<string>();
        foreach (var (name, version) in expected)
        {
            if (!actual.TryGetValue(name, out var named))
            {
                problems.Add($"{name} {version} is not in the library line");
            }
            else if (named != version)
            {
                problems.Add($"{name}: the library line says {named}, the project pins {version}");
            }
        }

        problems.AddRange(actual.Keys.Where(name => !expected.ContainsKey(name)).Select(name => $"{name} is in the library line and not in the project"));
        return problems;
    }

    /// <summary>The <c>ctx</c> members that system.md names and ctx-members.json does not have.</summary>
    public static List<string> UnknownContextMembers(string systemMd, string membersJson)
    {
        var groups = JsonDocument.Parse(membersJson).RootElement.EnumerateObject()
            .ToDictionary(group => group.Name, group => group.Value.EnumerateArray().Select(member => member.GetProperty("name").GetString()!).ToHashSet());

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match use in ContextUse().Matches(systemMd))
        {
            var parts = use.Value.Split('.');
            for (var i = 1; i < parts.Length; i++)
            {
                var group = string.Join('.', parts[..i]);
                if (!groups.TryGetValue(group, out var members) || !members.Contains(parts[i]))
                {
                    unknown.Add(string.Join('.', parts[..(i + 1)]));
                    break;
                }
            }
        }

        return [.. unknown];
    }

    /// <summary>The members of ctx-members.json that system.md never names, so the list the model reads stays complete.</summary>
    public static List<string> UnnamedContextMembers(string systemMd, string membersJson) =>
        JsonDocument.Parse(membersJson).RootElement.EnumerateObject()
            .SelectMany(group => group.Value.EnumerateArray().Select(member => group.Name + "." + member.GetProperty("name").GetString()))
            .Where(path => !Regex.IsMatch(systemMd, Regex.Escape(path) + @"(?![A-Za-z0-9_])"))
            .ToList();

    /// <summary>Text compared with whitespace collapsed, so that re-wrapping a sentence does not fail a test.</summary>
    public static string Flatten(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"^dependencies\s*=\s*\[(.*?)^\]", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex DependenciesBlock();

    [GeneratedRegex("\"([A-Za-z0-9_.-]+)(?:\\[[^\\]]*\\])?==([0-9][^\"]*)\"")]
    private static partial Regex Pin();

    [GeneratedRegex(@"\b([a-z][a-z0-9]*) (\d+\.\d+\.\d+)\b")]
    private static partial Regex Versioned();

    [GeneratedRegex(@"\bctx(?:\.[A-Za-z_][A-Za-z0-9_]*)*")]
    private static partial Regex ContextUse();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
