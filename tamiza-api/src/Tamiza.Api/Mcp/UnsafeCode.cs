using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tamiza.Api.Mcp;

/// <summary>A line of a script that an AI client may not save or test, and why.</summary>
public sealed record UnsafeFinding(int Line, string Found);

/// <summary>
/// Defense in depth for code that an AI client writes (design D9). Scripts run without a strong sandbox and the text of a form or of the
/// data could mislead a client into writing a script that sends data away. The editor only warns a person about such code; for a client
/// the server refuses code that imports a module scripts never need (the editor's own list), calls <c>__import__</c>, <c>eval</c>,
/// <c>exec</c>, <c>compile</c> or the builtin <c>open</c>, or holds a web address. A line scan that errs towards refusing, not a parser:
/// it is not isolation, and a determined script can still get around it.
/// </summary>
public static partial class UnsafeCode
{
    /// <summary>The modules scripts never need, from <c>python-modules.json</c> (a copy of the editor's, which a test keeps equal).</summary>
    public static IReadOnlySet<string> NeverModules { get; } = ReadNeverModules();

    public static IReadOnlyList<UnsafeFinding> Scan(string? code)
    {
        var findings = new List<UnsafeFinding>();
        if (string.IsNullOrEmpty(code))
        {
            return findings;
        }

        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var number = i + 1;

            foreach (Match match in ImportStatement().Matches(line))
            {
                foreach (var root in ImportedRoots(match))
                {
                    if (NeverModules.Contains(root))
                    {
                        findings.Add(new UnsafeFinding(number, $"import of {root}"));
                    }
                }
            }

            foreach (Match match in DangerousCall().Matches(line))
            {
                findings.Add(new UnsafeFinding(number, $"call of {match.Groups[1].Value}"));
            }

            if (WebAddress().IsMatch(line))
            {
                findings.Add(new UnsafeFinding(number, "web address"));
            }
        }

        return findings;
    }

    /// <summary>The top-level module of each name an import statement brings in: <c>import a.b, c as d</c> gives a and c; <c>from a.b import c</c> gives a.</summary>
    private static IEnumerable<string> ImportedRoots(Match match)
    {
        if (match.Groups["from"].Success)
        {
            yield return Root(match.Groups["from"].Value);
            yield break;
        }

        foreach (var part in match.Groups["names"].Value.Split(','))
        {
            var name = part.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(name))
            {
                yield return Root(name);
            }
        }
    }

    private static string Root(string module) => module.Split('.', 2)[0];

    // `import x` or `from x import y` at the start of a line or after a semicolon, at any indentation.
    [GeneratedRegex(@"(?:^|;)\s*(?:from\s+(?<from>[\w.]+)\s+import\b|import\s+(?<names>[\w., ]+))")]
    private static partial Regex ImportStatement();

    // A name that is not an attribute of something (`df.open(` and `re.compile(` are fine; `reopen(` and `evaluate(` are other names).
    // A parenthesized name is still the builtin: `(eval)('1')`.
    [GeneratedRegex(@"(?<![\w.])\(?\s*(__import__|eval|exec|compile|open)\s*\)?\s*\(")]
    private static partial Regex DangerousCall();

    [GeneratedRegex(@"\b(?:https?|ftp)://", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();

    private static IReadOnlySet<string> ReadNeverModules()
    {
        var assembly = typeof(UnsafeCode).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{typeof(UnsafeCode).Namespace}.python-modules.json")
            ?? throw new InvalidOperationException("python-modules.json is not embedded in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        return document.RootElement.GetProperty("never").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The refusal for code with findings; null when there are none.</summary>
    public static McpRefusal? RefusalFor(string? code)
    {
        var findings = Scan(code);
        if (findings.Count == 0)
        {
            return null;
        }

        var shown = string.Join("; ", findings.Take(5).Select(f => $"line {f.Line}: {f.Found}"));
        return new McpRefusal(
            McpRefusal.UnsafeCodeCode,
            $"The script was refused because it reaches outside the script ({shown}{(findings.Count > 5 ? "; …" : "")}). Scripts have no files or network and may not hold web addresses; remove those lines.",
            new Dictionary<string, object?> { ["lines"] = findings.Select(f => new { line = f.Line, found = f.Found }).ToList() });
    }
}
