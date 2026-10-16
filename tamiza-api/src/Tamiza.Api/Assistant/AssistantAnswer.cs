using System.Text;
using System.Text.RegularExpressions;

namespace Tamiza.Api.Assistant;

/// <summary>A block of code in an answer. <see cref="Complete"/> is false when the answer ended before the block's closing fence.</summary>
public sealed record AssistantBlock(string Code, bool Complete);

/// <summary>An answer split into its explanation and its code blocks (design D8).</summary>
/// <param name="Explanation">The text with each block replaced in place by a note such as <c>[proposed code: 34 lines]</c>.</param>
/// <param name="Blocks">The Python blocks, in the order they appeared.</param>
public sealed record SplitAnswer(string Explanation, IReadOnlyList<AssistantBlock> Blocks);

/// <summary>
/// Splits the text of an answer into fenced blocks and the explanation around them. A block is a <c>```python</c>, <c>```py</c> or
/// bare fence; fences of other languages stay in the explanation as written. The note that replaces a block is what the browser
/// sends back as the earlier turn, so the code that matters (the editor's text) is not sent twice.
/// </summary>
public static partial class AssistantAnswerParser
{
    private static readonly string[] PythonLanguages = ["", "python", "python3", "py"];

    public static SplitAnswer Split(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var explanation = new List<string>();
        var blocks = new List<AssistantBlock>();

        List<string>? code = null;
        var fenceLength = 0;
        var otherFence = 0;
        foreach (var line in lines)
        {
            if (code is not null)
            {
                if (IsClosing(line, fenceLength))
                {
                    Finish(complete: true);
                }
                else
                {
                    code.Add(line);
                }

                continue;
            }

            if (otherFence > 0)
            {
                explanation.Add(line);
                if (IsClosing(line, otherFence))
                {
                    otherFence = 0;
                }

                continue;
            }

            if (Opening().Match(line) is { Success: true } opening)
            {
                var length = opening.Groups[1].Length;
                var language = opening.Groups[2].Value.Trim().Split([' ', '\t'], 2)[0].ToLowerInvariant();
                if (PythonLanguages.Contains(language))
                {
                    code = [];
                    fenceLength = length;
                }
                else
                {
                    otherFence = length;
                    explanation.Add(line);
                }

                continue;
            }

            explanation.Add(line);
        }

        if (code is not null)
        {
            Finish(complete: false);
        }

        return new SplitAnswer(Blank().Replace(string.Join('\n', explanation), "\n\n").Trim(), blocks);

        void Finish(bool complete)
        {
            // Blank lines at the end of a block are not code. A block with no code at all is dropped.
            while (code!.Count > 0 && string.IsNullOrWhiteSpace(code[^1]))
            {
                code.RemoveAt(code.Count - 1);
            }

            if (code.Count > 0)
            {
                blocks.Add(new AssistantBlock(string.Join('\n', code), complete));
                explanation.Add($"[proposed code: {code.Count} lines]");
            }

            code = null;
        }
    }

    private static bool IsClosing(string line, int fenceLength) =>
        Closing().Match(line) is { Success: true } match && match.Groups[1].Length >= fenceLength;

    [GeneratedRegex(@"^ {0,3}(`{3,})([^`]*)$")]
    private static partial Regex Opening();

    [GeneratedRegex(@"^ {0,3}(`{3,})[ \t]*$")]
    private static partial Regex Closing();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blank();
}
