using NpgsqlTypes;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What an indicator's column map says about its columns, resolved against the published table (design D5 of add-small-sample-warnings):
/// a base only as a number, answers and no-data values only as text or true/false. A fact about a column the table lacks, or holds as
/// another type, is ignored here, so a widget never fails for it.
/// </summary>
/// <param name="BaseColumn">The column that counts the units each row stands for, or null.</param>
/// <param name="Answers">The columns that hold the answers of one question, by name, in ordinal order; the kind of answers (single or multiple choice) does not change the n.</param>
/// <param name="Missing">By column, the values that mean no data, as exact text.</param>
public sealed record IndicatorBases(string? BaseColumn, IReadOnlyList<string> Answers, IReadOnlyDictionary<string, IReadOnlyList<string>> Missing)
{
    /// <summary>
    /// The kind of each answer column, <see cref="AnswerKinds.Single"/> or <see cref="AnswerKinds.Multiple"/> (design D4 of improve-stat-cards).
    /// Null when the kinds were not given: the answers then only count the n, and a ratio over them is added up as it always was.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Kinds { get; init; }

    /// <summary>
    /// The answer columns of single choice that a ratio's values and totals count the base once across (design D4a): every declared
    /// column when all of them are single choice; none when the kinds are not given or one of them is multiple choice (mixed kinds are
    /// all taken as multiple choice, the safe side).
    /// </summary>
    public IReadOnlyList<string> SingleAnswers =>
        Kinds is not null && Answers.Count > 0 && Answers.All(a => Kinds.GetValueOrDefault(a) == AnswerKinds.Single) ? Answers : [];

    /// <summary>The answer columns of multiple choice that a ratio is never added up across (design D4b): every declared column unless all are single choice.</summary>
    public IReadOnlyList<string> MultipleAnswers => Kinds is null || Answers.Count == 0 || SingleAnswers.Count > 0 ? [] : Answers;

    /// <summary>Nothing is declared that applies to the table.</summary>
    public bool IsEmpty => BaseColumn is null && Answers.Count == 0 && Missing.Count == 0;

    /// <summary>
    /// The facts of <paramref name="metadata"/> that apply to <paramref name="columns"/>; null when none does, so an indicator without
    /// metadata is planned exactly as before.
    /// </summary>
    public static IndicatorBases? Resolve(IReadOnlyDictionary<string, IndicatorColumnMetadata>? metadata, IReadOnlyList<TableColumn> columns)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        string? baseColumn = null;
        var answers = new List<string>();
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!metadata.TryGetValue(column.Name, out var entry) || entry is null)
            {
                continue;
            }

            if (entry.Base is true && IndicatorValidation.IsNumeric(column))
            {
                baseColumn ??= column.Name;
            }

            if (column.Type is DataColumnType.Text or DataColumnType.Boolean)
            {
                if (!string.IsNullOrWhiteSpace(entry.Categories) && AnswerKinds.All.Contains(entry.Categories, StringComparer.Ordinal))
                {
                    answers.Add(column.Name);
                    kinds[column.Name] = entry.Categories;
                }

                if (entry.Missing is { Count: > 0 } values)
                {
                    missing[column.Name] = values.OfType<string>().ToList();
                }
            }
        }

        answers.Sort(StringComparer.Ordinal);
        var resolved = new IndicatorBases(baseColumn, answers, missing) { Kinds = kinds };
        return resolved.IsEmpty ? null : resolved;
    }
}

/// <summary>
/// The rows a widget leaves out as meaning no data (design D6 of add-small-sample-warnings): a widget that asks for it
/// (<see cref="WidgetConfig.MissingFooter"/>) and whose indicator declares values for its group or series column. A row is left out when
/// its group value is one of the group column's, or its series value one of the series column's, as exact text. A missing group or
/// series value ("(none)") is not a declared value: the <c>IS NOT NULL</c> keeps it.
/// </summary>
/// <param name="Parameters">The arrays <c>@ng</c> (group) and <c>@ns</c> (series) that <see cref="Left"/> names, one for each column that declares values.</param>
/// <param name="GroupLeft">The group column's part of the condition, or null.</param>
/// <param name="SeriesLeft">The series column's part of the condition, or null.</param>
/// <param name="Group">The quoted group column when it declares values, else null.</param>
/// <param name="Series">The quoted series column when it declares values, else null.</param>
public sealed record NoDataExclusion(IReadOnlyList<SqlParam> Parameters, string? GroupLeft, string? SeriesLeft, string? Group, string? Series)
{
    public const string GroupParameter = "ng";
    public const string SeriesParameter = "ns";

    /// <summary>The condition that holds for a row to leave out.</summary>
    public string Left => string.Join(" OR ", new[] { GroupLeft, SeriesLeft }.OfType<string>().Select(part => $"({part})"));

    /// <summary>What the plotted statements add to their <c>WHERE</c>: <c> AND NOT ({left})</c>.</summary>
    public string Condition => $" AND NOT ({Left})";

    /// <summary>
    /// The rows this widget leaves out; null when it does not ask for it, its kind does not take it, or its indicator declares no value
    /// for its group or series column (so every statement is the text it was). A query with no chart type is planned as a bar is.
    /// </summary>
    /// <param name="quote">Quotes a column of the table as the statement does.</param>
    public static NoDataExclusion? For(WidgetConfig config, string? chartType, IndicatorBases? bases, Func<string, string> quote)
    {
        if (config.MissingFooter is not true || bases is null || config.GroupBy is null || (chartType is not null && !ChartTypes.TakesMissingFooter(chartType)))
        {
            return null;
        }

        var parameters = new List<SqlParam>();
        string? Part(string? column, string parameter)
        {
            if (column is null || !bases.Missing.TryGetValue(column, out var values) || values.Count == 0)
            {
                return null;
            }

            parameters.Add(new SqlParam(parameter, NpgsqlDbType.Array | NpgsqlDbType.Text, values.ToArray()));
            return $"{quote(column)} IS NOT NULL AND {quote(column)}::text = ANY(@{parameter})";
        }

        var group = Part(config.GroupBy, GroupParameter);
        var series = Part(config.SeriesBy, SeriesParameter);
        return group is null && series is null
            ? null
            : new NoDataExclusion(
                parameters, group, series, group is null ? null : quote(config.GroupBy), series is null ? null : quote(config.SeriesBy!));
    }
}

/// <summary>
/// The statement that reports what a widget left out as no data (<see cref="BaseQueryBuilder.NoData"/>): one row, with the columns
/// <c>group values, series values, numerator, denominator, left-out sum, total sum, n</c>.
/// </summary>
/// <param name="IsRatio">Whether the widget plots a ratio: its value is the left-out numerator over the left-out denominator, and it has no share.</param>
public sealed record NoDataSql(IndicatorSql Sql, bool IsRatio)
{
    public const int GroupValues = 0;
    public const int SeriesValues = 1;
    public const int Numerator = 2;
    public const int Denominator = 3;
    public const int LeftOutSum = 4;
    public const int TotalSum = 5;
    public const int N = 6;
}

/// <summary>
/// The ordinals of the figure outputs a statement appends to its select list (design D5): <c>sum(numerator)</c> and
/// <c>sum(denominator)</c> of a ratio, <c>sum(base)</c> of an indicator that declares a base column, and the widget's n window. An output
/// the statement does not have has no ordinal.
/// </summary>
public sealed record SqlFigures(int? Numerator, int? Denominator, int? Base, int? WidgetN)
{
    /// <summary>Whether the statement has a figure output at all.</summary>
    public bool Any => Numerator is not null || Denominator is not null || Base is not null || WidgetN is not null;
}

/// <summary>The sums of a statement's figure outputs, over the rows of whatever it groups (design D5 of add-small-sample-warnings).</summary>
/// <param name="Numerator">A ratio's <c>sum(numerator)::double precision</c>; null for a measure.</param>
/// <param name="Denominator">A ratio's <c>sum(denominator)::double precision</c>; null for a measure.</param>
/// <param name="Base">The base column's <c>sum(base)::double precision</c>; null when the indicator declares none.</param>
/// <param name="NColumn">The quoted column a value's n is the sum of: the base column, else a ratio's denominator; null when a value has no n.</param>
public sealed record FigureSums(string? Numerator, string? Denominator, string? Base, string? NColumn)
{
    /// <summary>A measure with no base column: it adds no output.</summary>
    public bool Any => Numerator is not null || Base is not null;

    /// <summary>The sum expressions in output order: numerator, denominator, base.</summary>
    public IReadOnlyList<string> Outputs => new[] { Numerator, Denominator, Base }.OfType<string>().ToList();

    /// <summary>The ordinals of <see cref="Outputs"/> in a select list that already has <paramref name="existing"/> outputs.</summary>
    public SqlFigures Ordinals(int existing, bool widgetN)
    {
        var next = existing;
        int? numerator = Numerator is null ? null : next++;
        int? denominator = Denominator is null ? null : next++;
        int? baseOrdinal = Base is null ? null : next++;
        return new SqlFigures(numerator, denominator, baseOrdinal, widgetN ? next : null);
    }
}

/// <summary>
/// The base n of a widget's marks (design D2 to D5 of add-small-sample-warnings): the figures a statement appends to its select list, and
/// the statement that counts a base once across the answers of one question when the indicator declares answer columns.
/// </summary>
public static class BaseQueryBuilder
{
    /// <summary>The part of a counted statement that gives a group's n, or a cell's, a series' or the widget's.</summary>
    public static class Parts
    {
        public const int Group = 1;
        public const int Widget = 2;
    }

    /// <summary>The parts of the counted statement of a widget with a series.</summary>
    public static class SeriesCountParts
    {
        public const int Group = 1;
        public const int Cell = 2;
        public const int Series = 3;
        public const int All = 4;
    }

    /// <summary>
    /// The sums a statement over this config's rows appends. A ratio gives its numerator and denominator; the base column, when the
    /// indicator declares one, its sum; a measure with no base column gives nothing, so its statement is today's.
    /// </summary>
    /// <param name="quote">Quotes a column of the table as the statement does.</param>
    /// <param name="nested">
    /// The statement is the nested form of a ratio over answers of single choice (design D4a of improve-stat-cards): the sums are over
    /// the inner statement's columns, the numerator's and the base's added up, the denominator's the largest of its answers. The n column
    /// stays the table's, since the counted statement reads the table.
    /// </param>
    public static FigureSums Sums(WidgetConfig config, IndicatorBases? bases, Func<string, string> quote, bool nested = false)
    {
        var ratio = config.Ratio;
        var baseColumn = bases?.BaseColumn;
        var nColumn = baseColumn is not null ? quote(baseColumn) : ratio is not null ? quote(ratio.Denominator) : null;
        if (nested)
        {
            return new FigureSums(
                Sum(NestedRatio.Num), ratio is null ? null : $"max({NestedRatio.Den})::double precision", baseColumn is null ? null : Sum(NestedRatio.Base), nColumn);
        }

        return new FigureSums(
            ratio is null ? null : Sum(quote(ratio.Numerator)),
            ratio is null ? null : Sum(quote(ratio.Denominator)),
            baseColumn is null ? null : Sum(quote(baseColumn)),
            nColumn);
    }

    /// <summary>
    /// The check that refuses a ratio over answers of multiple choice (design D4b of improve-stat-cards), over the rows of the plotted
    /// statement: the first plotted value whose rows hold more than one answer, with how many distinct values each answer column has in
    /// it. A tile has one value, so no group; a widget with a series has one per group and series value.
    /// </summary>
    /// <param name="group">The quoted group column, or null for a stat tile.</param>
    /// <param name="series">The quoted series column of a widget with a series, else null.</param>
    /// <param name="filters">The widget's and the dashboard's filters, each introduced by <c> AND </c>, and the exclusion's.</param>
    /// <param name="parameters">The parameters of <paramref name="filters"/>.</param>
    /// <param name="also">A condition, introduced by <c> AND </c>, of the rows a fixed-domain widget plots, with its parameters.</param>
    public static CombinedCheck Combined(
        AnswerColumns answers, string? group, string? series, string from, string rowFilter, string filters, IReadOnlyList<SqlParam> parameters, string also = "")
    {
        var counts = string.Join(", ", answers.Multiple.Select(column => $"count(DISTINCT {column})"));
        var by = group is null ? "" : $" GROUP BY {group}{(series is null ? "" : ", " + series)}";
        var sql = $"SELECT {counts} FROM {from} WHERE {rowFilter}{filters}{also}{by} HAVING {answers.DistinctAnswers} > 1 LIMIT 1";
        return new CombinedCheck(new IndicatorSql(sql, [.. parameters]), answers.MultipleNames);
    }

    private static string Sum(string quotedColumn) => $"sum({quotedColumn})::double precision";

    /// <summary>
    /// Whether the widget's n is read from a window over the plotted groups (design D5): the indicator declares a base column and no
    /// answer column, so its groups are units counted once. With answer columns it is <see cref="Counted"/>; with neither the widget
    /// has none (design D4).
    /// </summary>
    public static bool WidgetWindow(IndicatorBases? bases) => bases is { BaseColumn: not null, Answers.Count: 0 };

    /// <summary>
    /// The window that gives a plain statement's widget n: the sum of its marks' n over every plotted group, which the window sees after
    /// <c>HAVING</c> and before <c>LIMIT</c>, so the groups past the row limit count too.
    /// </summary>
    public static string WidgetWindow(string quotedNColumn) => $"(sum(sum({quotedNColumn})) OVER ())::double precision";

    /// <summary>Whether a widget's n is counted once across answers with <see cref="Counted"/>: it declares answer columns and its marks have an n.</summary>
    public static bool CountsAnswers(IndicatorBases? bases, FigureSums sums) => bases is { Answers.Count: > 0 } && sums.NColumn is not null;

    /// <summary>
    /// The statement that reports what a widget left out as no data (design D6): over the same rows as the plotted statement without its
    /// <see cref="NoDataExclusion.Condition"/>, the left-out values that have rows, the sums of the left-out rows, the sum of every row,
    /// and the n of the left-out rows (the largest per-answer sum over them when the indicator declares answers, design D3). It always
    /// returns one row.
    /// </summary>
    /// <param name="measure">The measure column of a widget that plots a measure; null for a ratio.</param>
    /// <param name="filters">The widget's and the dashboard's filters, each introduced by <c> AND </c>, without the exclusion.</param>
    /// <param name="parameters">The parameters of <paramref name="filters"/>; the exclusion's are added here.</param>
    public static NoDataSql NoData(
        WidgetConfig config, string? measure, IndicatorBases? bases, NoDataExclusion exclusion, Func<string, string> quote, string from, string rowFilter,
        string filters, IReadOnlyList<SqlParam> parameters)
    {
        var left = exclusion.Left;
        string Filtered(string aggregate) => $"({aggregate} FILTER (WHERE {left}))::double precision";
        string Values(string? column, string? part) => column is null ? "NULL::text[]" : $"array_agg(DISTINCT {column}::text) FILTER (WHERE {part})";
        var ratio = config.Ratio;
        var baseColumn = bases?.BaseColumn;
        var nColumn = baseColumn is not null ? quote(baseColumn) : ratio is not null ? quote(ratio.Denominator) : null;
        string n;
        if (nColumn is null)
        {
            n = "NULL::double precision";
        }
        else if (bases is { Answers.Count: > 0 })
        {
            var answers = bases.Answers.Select(quote).ToList();
            var positions = string.Join(", ", Enumerable.Range(1, answers.Count));
            n = $"(SELECT max(s) FROM (SELECT {string.Join(", ", answers)}, sum({nColumn})::double precision AS s FROM {from} WHERE {rowFilter}{filters} AND ({left}) GROUP BY {positions}) y)";
        }
        else
        {
            n = Filtered($"sum({nColumn})");
        }

        var sql =
            $"""
            SELECT {Values(exclusion.Group, exclusion.GroupLeft)}, {Values(exclusion.Series, exclusion.SeriesLeft)},
              {(ratio is null ? "NULL::double precision" : Filtered($"sum({quote(ratio.Numerator)})"))},
              {(ratio is null ? "NULL::double precision" : Filtered($"sum({quote(ratio.Denominator)})"))},
              {(ratio is null ? Filtered($"sum({quote(measure!)})") : "NULL::double precision")},
              {(ratio is null ? $"sum({quote(measure!)})::double precision" : "NULL::double precision")},
              {n}
            FROM {from}
            WHERE {rowFilter}{filters}
            """;
        return new NoDataSql(new IndicatorSql(sql, [.. parameters, .. exclusion.Parameters]), ratio is not null);
    }

    /// <summary>
    /// The statement that counts the n of every mark and of the widget once across the answers (design D3, D5): for each, the largest of
    /// the sums of the base over the rows of one answer, an answer being one combination of every declared column. Part
    /// <see cref="Parts.Group"/> gives <c>group, n</c> of each group (a grouped widget only) and part <see cref="Parts.Widget"/> the
    /// widget's. It reads the same rows as the plotted statement, with the same <paramref name="rowFilter"/>, <paramref name="filters"/>
    /// and parameters, and has no join: the reader matches the groups by their text.
    /// </summary>
    /// <param name="group">The quoted group column, or null for a stat tile.</param>
    /// <param name="answers">The quoted answer columns, at least one.</param>
    /// <param name="n">The quoted column whose sum is the base: the base column, else a ratio's denominator.</param>
    /// <param name="from">The quoted <c>"schema"."table"</c>.</param>
    /// <param name="rowFilter">The condition that keeps the rows that have something to plot.</param>
    /// <param name="filters">The widget's and the dashboard's filters, each introduced by <c> AND </c>.</param>
    /// <param name="parameters">The plotted statement's parameters (those of the filters).</param>
    public static IndicatorSql Counted(
        string? group, IReadOnlyList<string> answers, string n, string from, string rowFilter, string filters, IReadOnlyList<SqlParam> parameters)
    {
        var list = string.Join(", ", answers);
        var positions = string.Join(", ", Enumerable.Range(1, answers.Count));
        var groupPositions = string.Join(", ", Enumerable.Range(1, answers.Count + 1));
        var widget =
            $"SELECT {Parts.Widget}, NULL::text, max(s) FROM (SELECT {list}, sum({n})::double precision AS s FROM {from} WHERE {rowFilter}{filters} GROUP BY {positions}) y";
        var sql = group is null
            ? widget
            : $"""
              SELECT {Parts.Group} AS part, g, max(s) FROM (SELECT {group}::text AS g, {list}, sum({n})::double precision AS s FROM {from} WHERE {rowFilter}{filters} GROUP BY {groupPositions}) x GROUP BY g
              UNION ALL
              {widget}
              """;
        return new IndicatorSql(sql, [.. parameters]);
    }

    /// <summary>
    /// The counted statement of a widget with a series: the same rows as its cells statement (<paramref name="source"/> is that
    /// statement's <c>src</c> query), with a part for each group, cell, series and the whole. A cell or series is keyed by the kind and
    /// text of its series value, as the cells statement writes them.
    /// </summary>
    /// <param name="source">The select that gives the widget's rows with <paramref name="seriesColumn"/> and <paramref name="kindColumn"/>.</param>
    /// <param name="parameters">The cells statement's parameters, those of the filters and the kept series values.</param>
    public static IndicatorSql CountedSeries(
        string group, string seriesColumn, string kindColumn, IReadOnlyList<string> answers, string n, string source, IReadOnlyList<SqlParam> parameters)
    {
        var list = string.Join(", ", answers);
        string Sum(string keys) => $"sum({n})::double precision AS s FROM src GROUP BY {keys}";
        var perAnswer = string.Join(", ", Enumerable.Range(1, answers.Count));
        string Positions(int keys) => string.Join(", ", Enumerable.Range(1, keys + answers.Count));
        var sql =
            $"""
            WITH src AS (
              {source}
            )
            SELECT {SeriesCountParts.Group} AS part, g, NULL::text AS k, NULL::text AS sv, max(s) FROM (SELECT {group}::text AS g, {list}, {Sum(Positions(1))}) x GROUP BY g
            UNION ALL
            SELECT {SeriesCountParts.Cell}, g, k, sv, max(s) FROM (SELECT {group}::text AS g, {kindColumn} AS k, {seriesColumn} AS sv, {list}, {Sum(Positions(3))}) x GROUP BY g, k, sv
            UNION ALL
            SELECT {SeriesCountParts.Series}, NULL::text, k, sv, max(s) FROM (SELECT {kindColumn} AS k, {seriesColumn} AS sv, {list}, {Sum(Positions(2))}) x GROUP BY k, sv
            UNION ALL
            SELECT {SeriesCountParts.All}, NULL::text, NULL::text, NULL::text, max(s) FROM (SELECT {list}, {Sum(perAnswer)}) x
            """;
        return new IndicatorSql(sql, [.. parameters]);
    }
}
