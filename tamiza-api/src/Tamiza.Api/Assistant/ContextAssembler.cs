using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Assistant;

/// <summary>
/// Builds the request the assistant sends (design D4): one system message of tagged blocks, most stable first, and the conversation's
/// turns as the browser sent them. The text is a pure function of the request and the project's data, so "Show what will be sent" and
/// the request itself cannot differ. <see cref="ContextTrimmer"/> keeps it under the configured size, and the <see cref="Masker"/>
/// makes the test facts safe.
/// </summary>
public sealed partial class ContextAssembler(ScriptInputsReader inputs, TamizaDbContext db)
{
    /// <summary>Question labels longer than this are cut when the context has to be trimmed (design D6).</summary>
    public const int LongLabel = 80;

    /// <summary>The bytes of script 1 that are kept when the context has to be trimmed (design D6).</summary>
    public const int Script1Limit = 16 * 1024;

    /// <summary>The members of <c>ctx</c> a script may use; names the masker keeps. A unit test compares them with the editor's <c>ctx-members.json</c>.</summary>
    public static readonly IReadOnlyList<string> CtxMembers =
    [
        "ctx", "project_name", "form", "log", "reference", "show",
        "questions", "choices", "columns", "label", "name", "column_label",
        "debug", "info", "warning", "error", "exception", "critical",
        "get", "items", "keys", "values",
    ];

    /// <summary>
    /// Loads what the context describes and assembles it under <paramref name="maxBytes"/>. Throws
    /// <see cref="DataTimeoutException"/> when the catalog read outlasts the statement timeout, and <see cref="ArgumentException"/> for an unknown kind.
    /// </summary>
    public async Task<AssembledContext> AssembleAsync(Guid projectId, ContextRequest request, long maxBytes, CancellationToken ct) =>
        Assemble(await LoadAsync(projectId, request.Kind, ct), request, maxBytes);

    /// <summary>Reads the tables the script receives, the form, the time zone and, for the other kinds, the saved script 1.</summary>
    public async Task<ContextSources> LoadAsync(Guid projectId, string kind, CancellationToken ct)
    {
        var tables = await inputs.ReadAsync(projectId, kind, ct);
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.FormFields, p.FormChoices, p.TimeZone, p.EnumeratorField })
            .SingleAsync(ct);
        var script1 = kind == "clean"
            ? null
            : await db.ScriptVersions.AsNoTracking()
                .Where(v => v.ProjectId == projectId && v.Kind == ScriptKind.Clean && v.Name == null)
                .OrderByDescending(v => v.Number)
                .Select(v => v.Code)
                .FirstOrDefaultAsync(ct);
        return new ContextSources(tables, project.FormFields, project.FormChoices, project.TimeZone, project.EnumeratorField, script1);
    }

    /// <summary>Assembles the request from already loaded data and trims it to <paramref name="maxBytes"/> (see <see cref="ContextTrimmer"/>).</summary>
    public static AssembledContext Assemble(ContextSources sources, ContextRequest request, long maxBytes) =>
        ContextTrimmer.Fit(sources, request, maxBytes);

    /// <summary>Renders the request with <paramref name="trim"/> applied. The trimmer calls it once per step.</summary>
    internal static Rendered Render(ContextSources sources, ContextRequest request, ContextTrim trim)
    {
        var kind = request.Kind;
        var turns = KeptTurns(request, trim);
        var mentions = Mentions(request.Draft, turns);
        var schema = SchemaBlock.Render(sources, kind, trim, mentions);

        var identifiers = new HashSet<string>(schema.Identifiers, StringComparer.Ordinal);
        identifiers.UnionWith(CtxMembers);
        identifiers.Add(ContractFunction(kind));
        if (request.Name is not null)
        {
            identifiers.Add(request.Name);
        }

        var blocks = new List<(string Tag, string Content)>
        {
            ("rules", Normalize(AssistantPrompts.System).Trim('\n')),
            ("examples", ExamplesBlock(kind, trim)),
            ("project", ProjectBlock(sources, request)),
            ("schema", schema.Text),
        };
        if (kind != "clean")
        {
            blocks.Add(("script1", Script1Block(sources.Script1, trim)));
        }

        var hasTest = request.Action == "fix" && request.Test is not null;
        if (hasTest)
        {
            // Masking runs on what is sent: the allow-list and the labels are those of the trimmed schema.
            var masker = new Masker(identifiers, schema.Labels, request.Draft);
            blocks.Add(("test", TestFacts.Render(request.Test!, request.Warning, masker)));
        }

        blocks.Add(("draft", DraftBlock(request, hasTest)));

        var system = string.Join("\n\n", blocks.Select(block => $"<{block.Tag}>\n{block.Content}\n</{block.Tag}>"));
        var sizes = blocks.ToDictionary(block => block.Tag, block => Encoding.UTF8.GetByteCount(block.Content), StringComparer.Ordinal);
        sizes["turns"] = turns.Sum(turn => Encoding.UTF8.GetByteCount(turn.Content));
        var bytes = Encoding.UTF8.GetByteCount(system) + sizes["turns"];
        var context = new AssembledContext(system, turns, trim.Omitted, sizes, bytes, Fits: true, hasTest);
        return new Rendered(context, schema.Stats, Examples(kind).Count, ExampleCount(kind, trim));
    }

    /// <summary>The turns that are sent: the conversation without the oldest pairs the trimmer left out.</summary>
    internal static IReadOnlyList<AssistantMessage> KeptTurns(ContextRequest request, ContextTrim trim) =>
        request.Messages.Skip(Math.Min(request.Messages.Count, trim.DroppedTurns * 2)).ToList();

    /// <summary>The lowercase words of the draft and of the turns: what "mentions" means to the trimmer.</summary>
    internal static HashSet<string> Mentions(string draft, IEnumerable<AssistantMessage> turns)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in turns.Select(t => t.Content).Append(draft))
        {
            foreach (Match match in Word().Matches(text))
            {
                words.Add(match.Value.ToLowerInvariant());
            }
        }

        return words;
    }

    private static string ContractFunction(string kind) => kind switch
    {
        "clean" => "clean",
        "output" => "prepare_output",
        _ => "compute",
    };

    private static IReadOnlyList<ScriptExample> Examples(string kind) => AssistantPrompts.Examples(kind);

    private static int ExampleCount(string kind, ContextTrim trim) => trim.FirstExampleOnly ? 1 : Examples(kind).Count;

    private static string ExamplesBlock(string kind, ContextTrim trim)
    {
        var text = new StringBuilder("Starter templates of this kind of script, as the editor offers them. Follow their structure and style.");
        foreach (var example in Examples(kind).Take(ExampleCount(kind, trim)))
        {
            text.Append("\n\n--- example: ").Append(example.Id).Append(" ---\n").Append(Normalize(example.Source).TrimEnd('\n'));
        }

        return text.ToString();
    }

    private static string ProjectBlock(ContextSources sources, ContextRequest request)
    {
        var text = new StringBuilder();
        text.Append(request.Kind switch
        {
            "clean" => "Script 1 (clean). It defines `clean(raw, ctx)`, receives the flattened tables in `raw` and returns a dictionary of table names to DataFrames: the clean tables.",
            "output" => "Script 2 (output). It defines `prepare_output(cleaned, ctx)`, receives the clean tables in `cleaned` and returns a dictionary of table names to DataFrames: the tables of the clean data dashboard.",
            _ => $"Indicator script `{request.Name}`. It defines `compute(cleaned, ctx)`, receives the clean tables in `cleaned` and returns one DataFrame, published as the table `{request.Name}`.",
        });

        if (request.Kind == "indicator" && request.Details is { } details)
        {
            text.Append("\nThe indicator's details, as the analyst has them now (saved or not):");
            AppendDetail(text, "label", details.Label);
            AppendDetail(text, "description", details.Description);
            AppendDetail(text, "unit", details.Unit);
            AppendDetail(text, "format", details.Format);
        }

        text.Append("\nProject time zone: ").Append(sources.TimeZone);
        text.Append("\nEnumerator question: ").Append(string.IsNullOrWhiteSpace(sources.EnumeratorField) ? "none set" : sources.EnumeratorField);
        text.Append("\nInput tables: ").Append(sources.Inputs.PublishedAt is { } at
            ? $"published {at.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}"
            : "not published yet (the project has not run)");
        return text.ToString();
    }

    private static void AppendDetail(StringBuilder text, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append("\n- ").Append(name).Append(": ").Append(Whitespace().Replace(value, " ").Trim());
        }
    }

    private static string Script1Block(string? script1, ContextTrim trim)
    {
        if (string.IsNullOrWhiteSpace(script1))
        {
            return "Script 1 has not been saved, so the clean tables are the flattened tables as they are.";
        }

        var code = Normalize(script1).TrimEnd('\n');
        if (trim.ShortScript1 && Encoding.UTF8.GetByteCount(code) > Script1Limit)
        {
            code = CutBytes(code, Script1Limit) + "\n[the rest of script 1 is left out]";
        }

        return "The saved script 1. Its result is the clean tables that this script receives.\n" + code;
    }

    /// <summary>The first <paramref name="limit"/> bytes of <paramref name="text"/>, cut at the end of a line when there is one.</summary>
    internal static string CutBytes(string text, int limit)
    {
        var length = 0;
        var end = 0;
        var lastLine = -1;
        for (var index = 0; index < text.Length; index += char.IsSurrogatePair(text, index) ? 2 : 1)
        {
            var size = Encoding.UTF8.GetByteCount(text.AsSpan(index, char.IsSurrogatePair(text, index) ? 2 : 1));
            if (length + size > limit)
            {
                break;
            }

            length += size;
            end = index + (char.IsSurrogatePair(text, index) ? 2 : 1);
            if (text[index] == '\n')
            {
                lastLine = index;
            }
        }

        return lastLine > 0 ? text[..lastLine] : text[..end];
    }

    private static string DraftBlock(ContextRequest request, bool hasTest)
    {
        var lines = Normalize(request.Draft).Split('\n');
        var text = new StringBuilder();
        switch (request.Action)
        {
            case "cell":
                var (first, last, name) = Cell(lines, request.FocusLine);
                text.Append($"Request: rewrite one cell. The cell is lines {first.ToString(CultureInfo.InvariantCulture)} to {last.ToString(CultureInfo.InvariantCulture)} below")
                    .Append(name.Length > 0 ? $", named \"{name}\"" : "")
                    .Append($"; it starts with the marker on line {first.ToString(CultureInfo.InvariantCulture)}. Answer with that cell only.\n");
                break;
            case "fix":
                text.Append(hasTest
                    ? "Request: fix the script so that the test described in <test> passes. This is the text that was tested. Answer with the whole script, but change only the lines that cause the failure. Keep every other line exactly as it is, in the same order, with the same names, comments and checks that raise an error. Do not restructure the script into cells, do not rename anything and do not add ctx.show.\n"
                    : "Request: fix the script. The analyst's test is no longer available, so there are no test facts. Answer with the whole script, but change only the lines that cause the failure. Keep every other line exactly as it is, in the same order, with the same names, comments and checks that raise an error. Do not restructure the script into cells, do not rename anything and do not add ctx.show.\n");
                break;
            default:
                text.Append("Request: write or change the whole script as the analyst's last message asks.\n");
                break;
        }

        text.Append("The editor's text, each line with its line number (the numbers are not part of the code):\n");
        if (string.IsNullOrEmpty(request.Draft))
        {
            text.Append("(the editor is empty)");
            return text.ToString();
        }

        var width = lines.Length.ToString(CultureInfo.InvariantCulture).Length;
        for (var index = 0; index < lines.Length; index++)
        {
            text.Append((index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width)).Append(" | ").Append(lines[index]);
            if (index < lines.Length - 1)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The first and last line (1-based) and the name of the cell whose marker is on <paramref name="focusLine"/>.</summary>
    internal static (int First, int Last, string Name) Cell(string[] lines, int? focusLine)
    {
        if (focusLine is not { } line || line < 1 || line > lines.Length || !Marker().IsMatch(lines[line - 1]))
        {
            throw new ArgumentException("The focus line is not a line that starts a cell with # %%.", nameof(focusLine));
        }

        var last = line;
        for (var index = line; index < lines.Length && !Marker().IsMatch(lines[index]); index++)
        {
            if (lines[index].Trim().Length > 0)
            {
                last = index + 1;
            }
        }

        var name = Marker().Match(lines[line - 1]).Groups[1].Value.Trim();
        return (line, last, name);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}_]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[ \t]*# %%(.*)$")]
    private static partial Regex Marker();
}

/// <summary>One rendering of a request, with the counts the trimmer compares to say what a step left out.</summary>
internal sealed record Rendered(AssembledContext Context, SchemaStats Schema, int Examples, int ExamplesShown);
