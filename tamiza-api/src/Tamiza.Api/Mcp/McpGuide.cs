using System.Text;
using Tamiza.Api.Assistant;

namespace Tamiza.Api.Mcp;

/// <summary>The texts the server gives AI clients: its instructions, the authoring guide and the prompt (design D14).</summary>
public static class McpGuide
{
    /// <summary>Sent to every client when it connects: what Tamiza is, the order of the work and the rules.</summary>
    public static string Instructions => InstructionsText.Value;

    private static readonly Lazy<string> InstructionsText = new(() => ReadText("Instructions.md"));

    /// <summary>The rule about addresses, appended to the contract because the server refuses code that holds one (design D9).</summary>
    public const string AddressRule =
        "## Addresses\n\n"
        + "Do not write web addresses (`http://`, `https://`, `ftp://`) in a script, not even in a comment or a string, and do not import modules that reach outside the script. "
        + "A script with either is refused when an AI client saves or tests it. Scripts have no files and no network: the tables arrive as arguments and the result is the return value.\n";

    /// <summary>The script contract, <c>ctx</c> members, libraries and conventions, and the starter templates of a kind (<c>clean</c>, <c>output</c> or <c>indicator</c>).</summary>
    public static object AuthoringGuide(string kind) => new
    {
        kind,
        contract = AssistantPrompts.Contract + "\n" + AddressRule,
        templates = AssistantPrompts.Examples(kind).Select(e => new { e.Id, source = e.Source }).ToList(),
    };

    /// <summary>The message the <c>describe_situation</c> prompt gives: the project, the situation and the order of work.</summary>
    public static string SituationPrompt(string projectName, Guid projectId, string situation)
    {
        var text = new StringBuilder();
        text.Append("Describe the following situation with the data of the Tamiza project \"").Append(projectName).Append("\" (id ").Append(projectId).Append("):\n\n")
            .Append(situation.Trim()).Append("\n\n")
            .Append("Work in the order of the server instructions. Start by reading the project and its form with get_project, get_form, get_tables and profile_table. ")
            .Append("Then propose the indicators you would build and wait for me to confirm them before you write any script. ")
            .Append("Test every script before you save it, run the project, build a dashboard with widgets and a filter, and finish by telling me what it shows.");
        return text.ToString();
    }

    private static string ReadText(string name)
    {
        var assembly = typeof(McpGuide).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{typeof(McpGuide).Namespace}.{name}")
            ?? throw new InvalidOperationException($"The MCP guide file '{name}' is not embedded in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
