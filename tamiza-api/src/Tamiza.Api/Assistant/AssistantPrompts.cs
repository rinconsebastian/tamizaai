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
    public const int Version = 2;

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
