using System.Text;

namespace Tamiza.Api.Assistant;

/// <summary>A starter template of one kind of script, as the editor offers it. The assistant sends it as an example.</summary>
public sealed record ScriptExample(string Id, string Source);

/// <summary>
/// The prompt kit: the rules, the templates and the masking vocabulary, embedded in the assembly (see the
/// <c>EmbeddedResource</c> items of Tamiza.Api.csproj). Edit any of these files together with <see cref="Version"/>.
/// </summary>
public static class AssistantPrompts
{
    /// <summary>
    /// Bumped with any change to <c>Prompts/system.md</c>, <c>Prompts/message-words.txt</c> or <c>Examples/**</c>, and stored in every
    /// usage record, so answers can be told apart by the instructions they were given.
    /// </summary>
    public const int Version = 4;

    /// <summary>
    /// The templates of each script kind in the order the editor offers them: the first one is the one kept when the context
    /// is trimmed. The copies in <c>Examples/&lt;kind&gt;/&lt;id&gt;.py</c> must equal the editor's own (a unit test checks it).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ExampleIds = new Dictionary<string, IReadOnlyList<string>>
    {
        ["clean"] = ["drop-test-submissions", "fix-types-and-recode"],
        ["output"] = ["columns-for-the-dashboard", "join-repeat-to-main"],
        ["indicator"] = ["count-by-group", "rate-by-group", "monthly-series"],
    };

    private static readonly Lazy<string> SystemText = new(() => ReadText("Prompts.system.md"));

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ScriptExample>>> ExampleSets = new(() =>
        ExampleIds.ToDictionary(
            kind => kind.Key,
            kind => (IReadOnlyList<ScriptExample>)kind.Value.Select(id => new ScriptExample(id, ReadText($"Examples.{kind.Key}.{id}.py"))).ToList()));

    private static readonly Lazy<IReadOnlySet<string>> Words = new(() =>
        ReadText("Prompts.message-words.txt")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>The rules block: what the model must know about scripts, the data and the answer's format.</summary>
    public static string System => SystemText.Value;

    /// <summary>The sections of <c>system.md</c> that describe scripts (language, contracts, data, <c>ctx</c>, libraries, cells) and not the assistant's answer.</summary>
    private static readonly string[] ContractSections =
        ["Language", "The contracts", "The indicator contract", "The data", "Choice labels", "`ctx`", "Libraries", "Cells"];

    private static readonly Lazy<string> ContractText = new(() => Sections(SystemText.Value, ContractSections));

    /// <summary>
    /// What an AI client needs to write a script: the contracts, the conventions of the data, <c>ctx</c>, the libraries and the cells,
    /// taken from the same rules the assistant follows, so the two cannot disagree. Without the assistant's answer format.
    /// </summary>
    public static string Contract => ContractText.Value;

    /// <summary>The headings of the sections of <c>system.md</c> (the text after <c>## </c>), in order. For tests.</summary>
    public static IReadOnlyList<string> SectionHeadings() => SplitSections(SystemText.Value).Select(s => s.Heading).ToList();

    private static string Sections(string text, IEnumerable<string> headings)
    {
        var all = SplitSections(text);
        var wanted = headings.ToList();
        var missing = wanted.Where(h => all.All(s => s.Heading != h)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"system.md has no section {string.Join(", ", missing)}.");
        }

        return string.Join("\n", all.Where(s => wanted.Contains(s.Heading)).Select(s => s.Text.TrimEnd('\n'))) + "\n";
    }

    private static List<(string Heading, string Text)> SplitSections(string text)
    {
        var sections = new List<(string, string)>();
        string? heading = null;
        var current = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    sections.Add((heading, current.ToString()));
                }

                heading = line[3..].Trim();
                current.Clear();
            }

            current.Append(line).Append('\n');
        }

        if (heading is not null)
        {
            sections.Add((heading, current.ToString()));
        }

        return sections;
    }

    /// <summary>The templates of a script kind (<c>clean</c>, <c>output</c> or <c>indicator</c>), first choice first.</summary>
    public static IReadOnlyList<ScriptExample> Examples(string kind) =>
        ExampleSets.Value.TryGetValue(kind, out var examples)
            ? examples
            : throw new ArgumentException($"Unknown script kind '{kind}'.", nameof(kind));

    /// <summary>The lowercase words of Python, library and Tamiza messages that the masker keeps (<c>Prompts/message-words.txt</c>).</summary>
    public static IReadOnlySet<string> MessageWords => Words.Value;

    /// <summary>The names of the embedded files of the kit, as the assembly knows them. For tests.</summary>
    public static IReadOnlyList<string> ResourceNames() =>
        typeof(AssistantPrompts).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(name => name[Prefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string Prefix => typeof(AssistantPrompts).Namespace + ".";

    private static string ReadText(string resource)
    {
        var assembly = typeof(AssistantPrompts).Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + resource)
            ?? throw new InvalidOperationException($"The prompt kit file '{resource}' is not embedded in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
