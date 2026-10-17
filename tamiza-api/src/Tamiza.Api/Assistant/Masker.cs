using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tamiza.Api.Assistant;

/// <summary>
/// Replaces, in text that came from a test (an error, a log line, the name of a table or a column), every word and number that
/// might be a respondent's data with <c>…</c> (design D5). It runs in two passes.
/// <list type="number">
/// <item>Each choice label and question label that the request carries is protected as a whole phrase, case-insensitively and
/// with its whitespace normalized. A label is never split into words that unlock the same words elsewhere.</item>
/// <item>Outside the protected phrases, a number is a run of digits joined by <c>. , : / -</c> or spaces (with an optional sign),
/// and is kept only when it has at most six digits in all; any other token (a maximal run of letters, digits and underscores)
/// is kept when it is an identifier of the schema, a word of the vocabulary, or a word of the editor's text that is not a word
/// of any label. A token with underscores is kept when every part is, and the digits of all its numeric parts count together.</item>
/// </list>
/// Quotes and punctuation stay, so <c>KeyError: '…'</c> keeps its shape, and runs of <c>…</c> separated only by spaces collapse into one.
/// </summary>
public sealed partial class Masker
{
    /// <summary>What replaces a word or number that is not kept.</summary>
    public const string Ellipsis = "…";

    /// <summary>A number is kept when it has at most this many digits.</summary>
    public const int MaxKeptDigits = 6;

    // One-letter tokens that carry no information about a person: the article, and the ending of a contraction ("doesn't").
    private static readonly HashSet<string> ContractionEndings = new(StringComparer.Ordinal) { "t", "s", "d", "m", "re", "ve", "ll" };

    private readonly HashSet<string> identifiers;
    private readonly IReadOnlySet<string> vocabulary;
    private readonly HashSet<string> draftWords;
    private readonly Regex? labelPhrases;

    /// <param name="identifiers">The names that the request sends in its project and schema blocks, the <c>ctx</c> members and the contract function. Never names taken from the test.</param>
    /// <param name="labels">The choice and question labels as the request sends them.</param>
    /// <param name="draft">The editor's text.</param>
    /// <param name="vocabulary">The lowercase words of Python, library and Tamiza messages; <see cref="AssistantPrompts.MessageWords"/> when omitted.</param>
    public Masker(IEnumerable<string> identifiers, IEnumerable<string> labels, string draft, IReadOnlySet<string>? vocabulary = null)
    {
        this.identifiers = new HashSet<string>(identifiers.Select(Lower), StringComparer.Ordinal);
        this.vocabulary = vocabulary ?? AssistantPrompts.MessageWords;

        var phrases = labels
            .Select(NormalizeSpace)
            .Where(label => label.Length > 0 && label.Any(char.IsLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var labelWords = phrases.SelectMany(Words).ToHashSet(StringComparer.Ordinal);

        // The draft's words are sent anyway, but a word that is part of a label does not unlock that word elsewhere.
        draftWords = Words(draft).Where(word => !labelWords.Contains(word) && !IsNumber(word)).ToHashSet(StringComparer.Ordinal);
        labelPhrases = phrases.Count == 0 ? null : BuildPhrases(phrases);
    }

    /// <summary>Masks <paramref name="text"/>; null and empty text come back as they are.</summary>
    public string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        text = text.Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(text.Length);
        var position = 0;
        if (labelPhrases is not null)
        {
            foreach (Match phrase in labelPhrases.Matches(text))
            {
                MaskPlain(text.AsSpan(position, phrase.Index - position), result);
                result.Append(phrase.ValueSpan);
                position = phrase.Index + phrase.Length;
            }
        }

        MaskPlain(text.AsSpan(position), result);
        return Collapse().Replace(result.ToString(), Ellipsis);
    }

    private void MaskPlain(ReadOnlySpan<char> span, StringBuilder result)
    {
        var segment = span.ToString();
        var tokens = Token().Matches(segment);
        var cursor = 0;
        var index = 0;
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (IsNumber(token.Value))
            {
                var last = index;
                var digits = token.Length;
                while (last + 1 < tokens.Count
                       && IsNumber(tokens[last + 1].Value)
                       && Joiner().IsMatch(segment.AsSpan(tokens[last].Index + tokens[last].Length, tokens[last + 1].Index - tokens[last].Index - tokens[last].Length)))
                {
                    last++;
                    digits += tokens[last].Length;
                }

                if (digits > MaxKeptDigits)
                {
                    var start = token.Index;
                    if (start > 0 && segment[start - 1] is '+' or '-' && (start == 1 || !IsWordChar(segment[start - 2])))
                    {
                        start--;
                    }

                    result.Append(segment, cursor, start - cursor).Append(Ellipsis);
                    cursor = tokens[last].Index + tokens[last].Length;
                }

                index = last + 1;
                continue;
            }

            if (!Keeps(token.Value, segment, token.Index))
            {
                result.Append(segment, cursor, token.Index - cursor).Append(Ellipsis);
                cursor = token.Index + token.Length;
            }

            index++;
        }

        result.Append(segment, cursor, segment.Length - cursor);
    }

    private bool Keeps(string token, string segment, int index)
    {
        var word = Lower(token);
        if (word == "a" || IsContractionEnding(word, segment, index))
        {
            return true;
        }

        if (AllowedWord(word))
        {
            return true;
        }

        if (!word.Contains('_'))
        {
            return false;
        }

        // A token with underscores: every part must be kept, and the digits of all its numeric parts count together.
        var digits = 0;
        foreach (var part in word.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsNumber(part))
            {
                digits += part.Length;
            }
            else if (part != "a" && !AllowedWord(part))
            {
                return false;
            }
        }

        return digits <= MaxKeptDigits;
    }

    private bool AllowedWord(string word) => identifiers.Contains(word) || vocabulary.Contains(word) || draftWords.Contains(word);

    private static bool IsContractionEnding(string word, string segment, int index) =>
        ContractionEndings.Contains(word) && index >= 2 && segment[index - 1] is '\'' or '’' && char.IsLetter(segment[index - 2]);

    private static bool IsNumber(string token) => token.Length > 0 && token.All(char.IsDigit);

    private static bool IsWordChar(char character) => char.IsLetterOrDigit(character) || character == '_' || char.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark;

    private static string Lower(string text) => text.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static IEnumerable<string> Words(string text) => Token().Matches(text.Normalize(NormalizationForm.FormC)).Select(match => match.Value.ToLowerInvariant());

    private static string NormalizeSpace(string label) => Whitespace().Replace(label.Normalize(NormalizationForm.FormC), " ").Trim();

    private static Regex BuildPhrases(IEnumerable<string> phrases)
    {
        // Longest first, so that the regex takes "Santa Ana de Los Caballeros" before "Santa Ana".
        var alternatives = phrases
            .OrderByDescending(phrase => phrase.Length)
            .Select(phrase =>
            {
                var body = string.Join(@"\s+", phrase.Split(' ').Select(Regex.Escape));
                var before = IsWordChar(phrase[0]) ? @"(?<![\p{L}\p{M}\p{N}_])" : "";
                var after = IsWordChar(phrase[^1]) ? @"(?![\p{L}\p{M}\p{N}_])" : "";
                return before + body + after;
            });
        return new Regex(string.Join('|', alternatives), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}_]+")]
    private static partial Regex Token();

    // What may sit between two groups of digits for them to be one number: a separator with or without spaces, or spaces, and a sign.
    [GeneratedRegex(@"^(?:\s*[.,:/\-]\s*|\s+)[+\-]?$")]
    private static partial Regex Joiner();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("…(?: *…)+")]
    private static partial Regex Collapse();
}
