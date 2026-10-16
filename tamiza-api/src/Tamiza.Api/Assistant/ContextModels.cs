using System.Text;
using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Assistant;

/// <summary>The indicator's details as the details fields hold them, saved or not.</summary>
public sealed record IndicatorDetails(string? Label, string? Description, string? Unit, string? Format);

/// <summary>
/// The dashboard warning a fix asks about, reduced to the parameters its kind keeps (design D3). <see cref="Measure"/> and
/// <see cref="Column"/> are names the test facts check against the test's tables before they use them.
/// </summary>
public sealed record AssistantWarning(string Key, string? Measure = null, string? Column = null, long? Count = null)
{
    /// <summary>The seven kinds of the indicator view's warnings.</summary>
    public static readonly IReadOnlyList<string> Keys =
        ["noValue", "emptyValues", "dimensionAsMeasure", "duplicateRows", "percentAboveOne", "noRows", "ratioIncomplete"];

    /// <summary>The longest <c>measure</c> or <c>column</c> kept.</summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// Builds the warning of kind <paramref name="key"/> from the params the browser sent, keeping only what the kind uses and
    /// dropping the rest (for example the largest value of <c>percentAboveOne</c>). Null for a key that is not one of <see cref="Keys"/>.
    /// </summary>
    public static AssistantWarning? Create(string? key, IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (key is null || !Keys.Contains(key, StringComparer.Ordinal))
        {
            return null;
        }

        string? Name(string name) =>
            parameters is not null && parameters.TryGetValue(name, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= MaxNameLength } text
                ? text
                : null;

        long? Count() =>
            parameters is not null && parameters.TryGetValue("count", out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0
                ? count
                : null;

        return key switch
        {
            "noValue" => new AssistantWarning(key, Measure: Name("measure")),
            "emptyValues" => new AssistantWarning(key, Measure: Name("measure"), Count: Count()),
            "dimensionAsMeasure" => new AssistantWarning(key, Column: Name("column")),
            "duplicateRows" => new AssistantWarning(key, Count: Count()),
            _ => new AssistantWarning(key),
        };
    }
}

/// <summary>What a request asks for, besides the project's own data (design D3, D4).</summary>
/// <param name="Kind"><c>clean</c>, <c>output</c> or <c>indicator</c>.</param>
/// <param name="Name">The indicator's name; null for the other kinds.</param>
/// <param name="Action"><c>script</c>, <c>cell</c> or <c>fix</c>.</param>
/// <param name="Messages">The conversation as the browser sent it, oldest first, ending with the analyst's request (or a blank one, or none, for the view before typing).</param>
/// <param name="Draft">The editor's text.</param>
/// <param name="FocusLine">For <c>cell</c>, the marker line of the cell to rewrite.</param>
/// <param name="Details">For an indicator, the details fields as they are now.</param>
/// <param name="Test">For <c>fix</c>, the analyst's own unexpired test of this draft; null when there is none (the request then goes without test facts).</param>
/// <param name="Warning">For <c>fix</c>, the dashboard warning asked about.</param>
public sealed record ContextRequest(
    string Kind,
    string? Name,
    string Action,
    IReadOnlyList<AssistantMessage> Messages,
    string Draft,
    int? FocusLine = null,
    IndicatorDetails? Details = null,
    ScriptTest? Test = null,
    AssistantWarning? Warning = null);

/// <summary>Everything the project holds that the context describes. Loaded by <see cref="ContextAssembler.LoadAsync"/>; built by hand in tests.</summary>
/// <param name="Inputs">The tables the script receives, the reference tables and the form's questions.</param>
/// <param name="Fields">The form's fields, which carry the choice list and the "or other" mark of select questions.</param>
/// <param name="Choices">The form's choice lists.</param>
/// <param name="TimeZone">The project's IANA time zone.</param>
/// <param name="EnumeratorField">The path of the question that identifies the enumerator, or null.</param>
/// <param name="Script1">The saved script 1, or null when it was never saved or was removed.</param>
public sealed record ContextSources(
    ScriptInputs Inputs,
    IReadOnlyList<FormField> Fields,
    IReadOnlyList<FormChoice> Choices,
    string TimeZone,
    string? EnumeratorField,
    string? Script1);

/// <summary>
/// What the trimmer has left out so far (design D6). The steps are applied in the order of the properties; each one that changed
/// something adds an entry to <see cref="Omitted"/>.
/// </summary>
public sealed record ContextTrim
{
    /// <summary>Earlier turns left out, in pairs of a request and its answer, oldest first.</summary>
    public int DroppedTurns { get; init; }

    /// <summary>Choice lists whose name, question or columns neither the draft nor the messages mention are left out.</summary>
    public bool UnmentionedLists { get; init; }

    /// <summary>Tables other than <c>main</c> that neither the draft nor the messages mention are left out.</summary>
    public bool UnmentionedTables { get; init; }

    /// <summary>Question labels beyond <see cref="ContextAssembler.LongLabel"/> characters are cut.</summary>
    public bool ShortLabels { get; init; }

    /// <summary>Script 1 beyond <see cref="ContextAssembler.Script1Limit"/> bytes is cut.</summary>
    public bool ShortScript1 { get; init; }

    /// <summary>Only the first example is kept.</summary>
    public bool FirstExampleOnly { get; init; }

    /// <summary>One entry per step that left something out, in the order the steps ran.</summary>
    public IReadOnlyList<string> Omitted { get; init; } = [];
}

/// <summary>
/// The request the assistant would send: the system message, built from the blocks of design D4, and the conversation's turns.
/// <see cref="Text"/> is what "Show what will be sent" displays; <see cref="Messages"/> is what the provider receives.
/// </summary>
/// <param name="System">The system message: the tagged blocks, <c>&lt;rules&gt;</c> to <c>&lt;draft&gt;</c>.</param>
/// <param name="Turns">The conversation after the trimmer's cuts, starting with a <c>user</c> turn.</param>
/// <param name="Omitted">What was left out to fit, for the context view and the record.</param>
/// <param name="BlockBytes">The UTF-8 size of each block (<c>rules</c>, <c>examples</c>, <c>project</c>, <c>schema</c>, <c>script1</c>, <c>test</c>, <c>draft</c>) and of the <c>turns</c>, for the usage record.</param>
/// <param name="Fits">False when the request is still over the configured size after everything that may be left out was left out.</param>
/// <param name="HasTest">Whether a test's facts are in the request.</param>
public sealed record AssembledContext(
    string System,
    IReadOnlyList<AssistantMessage> Turns,
    IReadOnlyList<string> Omitted,
    IReadOnlyDictionary<string, int> BlockBytes,
    long Bytes,
    bool Fits,
    bool HasTest)
{
    /// <summary>The messages for <see cref="AssistantClient"/>: the system message, then the turns.</summary>
    public IReadOnlyList<AssistantMessage> Messages => [new AssistantMessage("system", System), .. Turns];

    /// <summary>The whole request as text: each message under its role.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            foreach (var message in Messages)
            {
                if (text.Length > 0)
                {
                    text.Append("\n\n");
                }

                text.Append("=== ").Append(message.Role).Append(" ===\n").Append(message.Content);
            }

            return text.ToString();
        }
    }
}
