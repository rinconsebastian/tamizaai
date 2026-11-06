using System.Globalization;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Indicators;

/// <summary>
/// The answer columns a widget's ratio is planned over (design D4 of improve-stat-cards): the columns of single choice, whose answers a
/// ratio counts its base once across (<see cref="Single"/>), and those of multiple choice, which a ratio is never added up across
/// (<see cref="Multiple"/>). Both are quoted for the statement; at most one of them is not empty, because an indicator whose declared
/// columns mix the two kinds has all of them taken as multiple choice. Empty for a measure, a widget the rule does not touch, and an
/// indicator that declares nothing, so every statement of those is the text it always was.
/// </summary>
/// <param name="Single">The quoted columns of single choice.</param>
/// <param name="Multiple">The quoted columns of multiple choice, in the table's column order.</param>
/// <param name="MultipleNames">The same columns by name, in the same order.</param>
public sealed record AnswerColumns(IReadOnlyList<string> Single, IReadOnlyList<string> Multiple, IReadOnlyList<string> MultipleNames)
{
    public static readonly AnswerColumns None = new([], [], []);

    /// <summary>A ratio's values and totals over several answers count the base once (design D4a).</summary>
    public bool Corrects => Single.Count > 0;

    /// <summary>A ratio's values that would add up several answers are refused (design D4b).</summary>
    public bool Refuses => Multiple.Count > 0;

    /// <summary>
    /// The columns a query over this config takes. A ratio only: a measure, a statistic and a box plot never reach here, and the per-row
    /// histogram (<paramref name="perRow"/>) has no sum to correct.
    /// </summary>
    /// <param name="columns">The table's columns in their order.</param>
    /// <param name="quote">Quotes a column of the table as the statement does.</param>
    public static AnswerColumns Of(WidgetConfig config, bool perRow, IndicatorBases? bases, IReadOnlyList<TableColumn> columns, Func<string, string> quote)
    {
        if (config.Ratio is null || perRow || bases is null)
        {
            return None;
        }

        if (bases.SingleAnswers is { Count: > 0 } single)
        {
            return new AnswerColumns(single.Select(quote).ToList(), [], []);
        }

        if (bases.MultipleAnswers is { Count: > 0 } multiple)
        {
            var inOrder = columns.Select(c => c.Name).Where(multiple.Contains).ToList();
            return new AnswerColumns([], inOrder.Select(quote).ToList(), inOrder);
        }

        return None;
    }

    /// <summary>
    /// <c>count(DISTINCT (answers))</c>: how many answers the rows of a mark hold, the columns taken together as the answers of one
    /// question.
    /// </summary>
    public string DistinctAnswers => $"count(DISTINCT ({string.Join(", ", Multiple)}))";

    /// <summary>
    /// A value that spans several answers has no value (design D4b): <paramref name="aggregate"/> when the rows hold one answer, else
    /// NULL. The aggregate itself when no column is of multiple choice.
    /// </summary>
    public string OneAnswerOnly(string aggregate) => Refuses ? $"CASE WHEN {DistinctAnswers} > 1 THEN NULL ELSE {aggregate} END" : aggregate;
}

/// <summary>
/// The check that refuses a ratio over answers of multiple choice (design D4b of improve-stat-cards): over the rows a widget plots, one
/// row per plotted value that holds several answers. No row means every value holds one.
/// </summary>
/// <param name="Sql">Selects <c>count(DISTINCT column)</c> of each of <paramref name="Columns"/>, in that order, of the first value that spans answers.</param>
/// <param name="Columns">The multiple-choice columns by name, in the table's column order.</param>
public sealed record CombinedCheck(IndicatorSql Sql, IReadOnlyList<string> Columns)
{
    /// <summary>The column with the most distinct values in the row; the last in the table's column order on a tie.</summary>
    public string Named(IReadOnlyList<long> distinct)
    {
        var best = 0;
        for (var i = 1; i < Columns.Count; i++)
        {
            if (distinct[i] >= distinct[best])
            {
                best = i;
            }
        }

        return Columns[best];
    }
}

/// <summary>
/// The nested form of a ratio over answers of single choice (design D4a of improve-stat-cards): an inner statement sums the numerator
/// and the denominator of each mark and each answer, and the outer one divides the sum of the numerators by the largest denominator sum,
/// so the base is counted once across the answers. The inner statement carries what the outer one still needs under names that no table
/// column can have (every one starts with two underscores).
/// </summary>
public static class NestedRatio
{
    public const string Num = "\"__num\"";
    public const string Den = "\"__den\"";
    public const string Rows = "\"__n\"";
    public const string Base = "\"__base\"";
    public const string Geometry = "\"__geom\"";
    public const string Sort = "\"__sort\"";
    public const string Group = "\"__g\"";

    /// <summary>The ratio over the inner statement's rows: <c>sum(num) / max(den)</c> over its answers.</summary>
    public const string Value = $"sum({Num})::double precision / nullif(max({Den}), 0)";

    /// <summary>The inner sums: the numerator, the denominator and the number of table rows of each mark and answer.</summary>
    public static string InnerSums(string numerator, string denominator) =>
        $"sum({numerator}) AS {Num}, sum({denominator}) AS {Den}, count(*) AS {Rows}";

    /// <summary>
    /// The answer columns as the inner statement selects them (<c>col AS "__a0"</c>) and groups by (<c>col</c>), leaving out
    /// <paramref name="group"/> when it is one of them, since the group is already a key.
    /// </summary>
    public static (IReadOnlyList<string> Select, IReadOnlyList<string> Keys) Answers(IReadOnlyList<string> single, string? group)
    {
        var kept = single.Where(c => c != group).ToList();
        return (kept.Select((c, i) => string.Create(CultureInfo.InvariantCulture, $"{c} AS \"__a{i}\"")).ToList(), kept);
    }
}

/// <summary>
/// The order of the groups of the nested form (design D4a of improve-stat-cards): the group is the inner statement's, and a sort by a
/// column reads the aggregate that the inner statement carries as <see cref="NestedRatio.Sort"/>, taken again over the answers.
/// </summary>
/// <param name="Group">The group as the outer statement sees it: <see cref="NestedRatio.Group"/>, or the series statement's <c>g</c>.</param>
public sealed record NestedSort(string Group)
{
    /// <summary>The outer key of a sort by <paramref name="column"/>: its smallest carried key, true/false being true only when every one is.</summary>
    public string SortKey(TableColumn column) =>
        column.Type == DataColumnType.Boolean ? $"bool_and({NestedRatio.Sort})" : $"min({NestedRatio.Sort})";

    /// <summary>What the inner statement carries for a sort by a column (the key as an unnested statement computes it), or null when the order needs none.</summary>
    public static string? Carry(WidgetConfig config, IReadOnlyDictionary<string, TableColumn> byName) =>
        config.Sort is { By: SortValues.ByColumn, Column: { } column } sort && byName.TryGetValue(column, out var found)
            ? $", {IndicatorQueryBuilder.ColumnSortKey(sort, found)} AS {NestedRatio.Sort}"
            : null;
}
