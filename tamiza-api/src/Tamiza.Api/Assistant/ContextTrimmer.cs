using System.Globalization;

namespace Tamiza.Api.Assistant;

/// <summary>
/// Keeps a request under <c>TAMIZA_ASSISTANT_MAX_CONTEXT_KB</c> (design D6). It measures the UTF-8 size of the system message plus the
/// turns and, until the request fits, leaves out in this order: earlier turns (in pairs of a request and its answer, always keeping
/// the last user turn, the answer before it and the request that answer belongs to); choice lists that neither the draft nor the
/// messages mention; tables other than <c>main</c> that they do not mention; question labels beyond 80 characters; script 1 beyond
/// its first 16 KB; and all examples but the first. Each step that left something out adds an entry to
/// <see cref="AssembledContext.Omitted"/>. The draft is never shortened. Masking runs after trimming, because the request is
/// rendered again for each step and the masker is built from what that rendering holds.
/// </summary>
public static class ContextTrimmer
{
    /// <summary>The turns that are never left out: the last user turn, the answer before it and the request that answer belongs to.</summary>
    private const int ProtectedTurns = 3;

    private sealed record Step(Func<ContextTrim, ContextTrim> Apply, Func<Rendered, Rendered, string?> Describe);

    private static readonly Step[] Steps =
    [
        new(t => t with { UnmentionedLists = true },
            (before, after) => after.Schema.ListsShown < before.Schema.ListsShown
                ? $"choice lists: {Count(after.Schema.Lists - after.Schema.ListsShown)} of {Count(after.Schema.Lists)} left out"
                : null),
        new(t => t with { UnmentionedTables = true },
            (before, after) => after.Schema.TablesShown < before.Schema.TablesShown
                ? $"tables: {Count(after.Schema.Tables - after.Schema.TablesShown)} of {Count(after.Schema.Tables)} left out"
                : null),
        new(t => t with { ShortLabels = true },
            (before, after) => after.Schema.LabelsCut > before.Schema.LabelsCut
                ? $"question labels: {Count(after.Schema.LabelsCut)} cut to {Count(ContextAssembler.LongLabel)} characters"
                : null),
        new(t => t with { ShortScript1 = true },
            (before, after) => after.Context.BlockBytes.GetValueOrDefault("script1") < before.Context.BlockBytes.GetValueOrDefault("script1")
                ? $"script 1: cut after {Count(ContextAssembler.Script1Limit / 1024)} KB"
                : null),
        new(t => t with { FirstExampleOnly = true },
            (before, after) => after.ExamplesShown < before.ExamplesShown
                ? $"examples: {Count(after.Examples - after.ExamplesShown)} of {Count(after.Examples)} left out"
                : null),
    ];

    /// <summary>Renders the request and trims it. <see cref="AssembledContext.Fits"/> is false when it is still over <paramref name="maxBytes"/> after the last step.</summary>
    public static AssembledContext Fit(ContextSources sources, ContextRequest request, long maxBytes)
    {
        var trim = new ContextTrim();
        var rendered = ContextAssembler.Render(sources, request, trim);

        // 1. Earlier turns, a pair at a time.
        var dropped = 0;
        while (rendered.Context.Bytes > maxBytes && request.Messages.Count - (trim.DroppedTurns * 2) > ProtectedTurns)
        {
            trim = trim with { DroppedTurns = trim.DroppedTurns + 1 };
            dropped += 2;
            rendered = ContextAssembler.Render(sources, request, trim);
        }

        if (dropped > 0)
        {
            trim = trim with { Omitted = [.. trim.Omitted, $"earlier turns: {Count(dropped)} of {Count(request.Messages.Count)} left out"] };
            rendered = rendered with { Context = rendered.Context with { Omitted = trim.Omitted } };
        }

        // 2 to 6. A step is tried only while the request does not fit, and kept only when it left something out.
        foreach (var step in Steps)
        {
            if (rendered.Context.Bytes <= maxBytes)
            {
                break;
            }

            var candidate = step.Apply(trim);
            var next = ContextAssembler.Render(sources, request, candidate);
            if (step.Describe(rendered, next) is not { } entry)
            {
                continue;
            }

            trim = candidate with { Omitted = [.. trim.Omitted, entry] };
            rendered = next with { Context = next.Context with { Omitted = trim.Omitted } };
        }

        return rendered.Context with { Fits = rendered.Context.Bytes <= maxBytes };
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
