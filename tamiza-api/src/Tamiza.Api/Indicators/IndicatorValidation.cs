using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What a widget's fields must satisfy. The checks that need no table run on every request; the checks against
/// the published table's columns run when the table is published (<see cref="CheckAgainstColumns"/>).
/// </summary>
public static class IndicatorValidation
{
    public const int MaxTitleLength = 200;
    public const int GridColumns = 12;
    public const int MaxHeight = 40;

    private static readonly string[] FilterOps = ["contains", "equals", "in", "range", "empty", "notEmpty"];

    /// <summary>The size a new widget of this type gets.</summary>
    public static (int W, int H) DefaultSize(string chartType) => chartType switch
    {
        ChartTypes.Stat => (3, 2),
        ChartTypes.Map => (6, 6),
        ChartTypes.Heatmap or ChartTypes.Pyramid or ChartTypes.Crosstab => (6, 5),
        ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line or ChartTypes.Histogram or ChartTypes.Pie or ChartTypes.Likert or ChartTypes.Boxplot => (6, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(chartType), chartType, "Not a chart type."),
    };

    public static void CheckTitle(string? title, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("title", "The title is required.");
        }
        else if (title.Trim().Length > MaxTitleLength)
        {
            errors.Add("title", $"The title is longer than {MaxTitleLength} characters.");
        }
    }

    public static void CheckChartType(string? chartType, FieldErrors errors)
    {
        if (!ChartTypes.IsKnown(chartType))
        {
            errors.Add("chartType", $"The chart type must be one of {string.Join(", ", ChartTypes.All)}.");
        }
    }

    /// <summary>
    /// The checks of a widget's position and size on the grid. <paramref name="prefix"/> is <c>widgets[i].</c> for one entry of the layout
    /// of several widgets (design D8 of dashboard versions), so its errors name the entry.
    /// </summary>
    public static void CheckLayout(WidgetLayout? layout, FieldErrors errors, string prefix = "")
    {
        if (layout is null)
        {
            errors.Add(prefix + "layout", "The layout is required.");
            return;
        }

        if (layout.X < 0)
        {
            errors.Add(prefix + "layout.x", "x must be 0 or more.");
        }

        if (layout.Y < 0)
        {
            errors.Add(prefix + "layout.y", "y must be 0 or more.");
        }

        if (layout.W < 1)
        {
            errors.Add(prefix + "layout.w", "w must be 1 or more.");
        }

        if (layout.H is < 1 or > MaxHeight)
        {
            errors.Add(prefix + "layout.h", $"h must be between 1 and {MaxHeight}.");
        }

        if (layout.X >= 0 && layout.W >= 1 && (long)layout.X + layout.W > GridColumns)
        {
            errors.Add(prefix + "layout.w", $"x + w must not pass the {GridColumns} columns of the grid.");
        }
    }

    /// <summary>
    /// The checks of a widget's query that need no table. <paramref name="chartType"/> is null when the caller has none, as the query
    /// endpoint may; then the per-type constraints are not checked. <paramref name="prefix"/> is <c>config.</c> inside a widget.
    /// </summary>
    public static void CheckConfig(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (string.IsNullOrEmpty(config.Indicator))
        {
            errors.Add(prefix + "indicator", "Choose an indicator.");
        }
        else if (!ColumnNames.IsValidTableName(config.Indicator))
        {
            errors.Add(prefix + "indicator", "The indicator is not a valid indicator name.");
        }

        if (config.Measure is not null && config.Ratio is not null)
        {
            errors.Add(prefix + "measure", "Plot a measure or a ratio, not both.");
        }

        CheckColumnName(config.Measure, prefix + "measure", "measure", errors);
        if (config.Ratio is { } ratio)
        {
            if (string.IsNullOrEmpty(ratio.Numerator))
            {
                errors.Add(prefix + "ratio.numerator", "Choose the numerator column.");
            }
            else
            {
                CheckColumnName(ratio.Numerator, prefix + "ratio.numerator", "numerator", errors);
            }

            if (string.IsNullOrEmpty(ratio.Denominator))
            {
                errors.Add(prefix + "ratio.denominator", "Choose the denominator column.");
            }
            else
            {
                CheckColumnName(ratio.Denominator, prefix + "ratio.denominator", "denominator", errors);
            }
        }

        CheckColumnName(config.GroupBy, prefix + "groupBy", "group column", errors);
        if (chartType is not null && ChartTypes.NeedsGroup(chartType) && config.GroupBy is null)
        {
            errors.Add(prefix + "groupBy", $"A {chartType} widget needs a column to group by.");
        }

        CheckSeries(config, chartType, prefix, errors);

        if (config.Buckets is { } buckets && (buckets < 1 || buckets > WidgetConfig.MaxBuckets))
        {
            errors.Add(prefix + "buckets", $"The number of buckets must be between 1 and {WidgetConfig.MaxBuckets}.");
        }

        if (chartType == ChartTypes.Map && config.GeometryColumn is null)
        {
            errors.Add(prefix + "geometryColumn", "A map widget needs a geometry column.");
        }
        else
        {
            CheckColumnName(config.GeometryColumn, prefix + "geometryColumn", "geometry column", errors);
        }

        CheckFilterShape(config.Filters, prefix, errors);
        CheckSort(config, chartType, prefix, errors);
        CheckTooltip(config, chartType, prefix, errors);
        CheckBasemap(config.Basemap, chartType, prefix, errors);
        CheckIgnoredDashboardFilters(config.IgnoredDashboardFilters, prefix, errors);
        CheckDisplay(config, chartType, prefix, errors);
        CheckMissingFooter(config, chartType, prefix, errors);
        CheckStatistic(config, chartType, prefix, errors);
    }

    /// <summary>
    /// A stat tile's statistic (design D2 of improve-stat-cards): one of the four, and only on a stat tile. A query with no chart type
    /// that names one is refused too, unlike the other per-kind rules, since the planner needs the kind to know that it must not sum.
    /// </summary>
    private static void CheckStatistic(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (config.Statistic is not { } statistic)
        {
            return;
        }

        var key = prefix + "statistic";
        if (!StatStatistics.IsKnown(statistic))
        {
            errors.Add(key, "The statistic must be median, p25, p75 or mean.");
        }

        if (chartType is null)
        {
            errors.Add(key, "A statistic needs the chart type stat.");
        }
        else if (chartType != ChartTypes.Stat)
        {
            errors.Add(key, "Only a stat tile has a statistic.");
        }
    }

    /// <summary>
    /// A stat tile's secondary lines (design D3 of improve-stat-cards): only <c>n</c> and <c>unfiltered</c>, each once. The kind rule is
    /// skipped when <paramref name="chartType"/> is null, as the other per-kind rules are.
    /// </summary>
    private static void CheckSecondary(IReadOnlyList<string>? lines, string? chartType, string prefix, FieldErrors errors)
    {
        if (lines is null)
        {
            return;
        }

        var key = prefix + "display.secondary";
        if (lines.Any(line => !StatSecondaryLines.IsKnown(line)))
        {
            errors.Add(key, "Each secondary line must be 'n' or 'unfiltered'.");
        }

        if (lines.Where(line => line is not null).GroupBy(line => line, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            errors.Add(key, "A secondary line is listed twice.");
        }

        if (chartType is not null && chartType != ChartTypes.Stat)
        {
            errors.Add(key, "Only a stat tile has secondary lines.");
        }
    }

    /// <summary>
    /// Leaving out the values that mean no data (design D6 of add-small-sample-warnings): only the kinds that group their rows and draw
    /// a mark for each take it. It is not checked against the indicator's metadata: with nothing declared for the group and series columns
    /// it does nothing. Skipped when <paramref name="chartType"/> is null, as the other per-kind rules are.
    /// </summary>
    private static void CheckMissingFooter(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (config.MissingFooter is not null && chartType is not null && !ChartTypes.TakesMissingFooter(chartType))
        {
            errors.Add(prefix + "missingFooter", "Only bar, column, line, pie, heat map, population pyramid, Likert and cross table widgets can leave out values that mean no data.");
        }
    }

    /// <summary>
    /// How a widget is drawn (design D4 of dashboard palettes): a color is a number of the dashboard's palette, for the charts that
    /// draw one color, and a scale is one of the sequential scales, for a map and a heat map. Neither is checked against a palette or
    /// a table. Per-kind rules are skipped when <paramref name="chartType"/> is null, as the query endpoint may have none.
    /// </summary>
    private static void CheckDisplay(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (config.Display is not { } display)
        {
            return;
        }

        if (display.Color is { } color)
        {
            if (!Palettes.IsColor(color))
            {
                errors.Add(prefix + "display.color", $"The color must be a number from 1 to {Palettes.ColorCount}.");
            }
            else if (chartType is not null && chartType is not (ChartTypes.Histogram or ChartTypes.Boxplot) && !ChartTypes.TakesSeries(chartType))
            {
                errors.Add(prefix + "display.color", $"A {chartType} widget has no color of its own.");
            }
            else if (chartType is not null && config.SeriesBy is not null && ChartTypes.TakesSeries(chartType))
            {
                errors.Add(prefix + "display.color", "A widget with a series takes its colors from its series.");
            }
        }

        if (display.Orientation is { } orientation)
        {
            if (!BoxOrientations.IsKnown(orientation))
            {
                errors.Add(prefix + "display.orientation", $"The orientation must be '{BoxOrientations.Vertical}' or '{BoxOrientations.Horizontal}'.");
            }
            else if (chartType is not null && chartType != ChartTypes.Boxplot)
            {
                errors.Add(prefix + "display.orientation", "Only a box plot has an orientation.");
            }
        }

        if (display.Ramp is { } ramp)
        {
            if (!Palettes.IsScale(ramp))
            {
                errors.Add(prefix + "display.ramp", $"The scale must be one of {string.Join(", ", Palettes.Scales)}.");
            }
            else if (chartType is not null && chartType is not (ChartTypes.Map or ChartTypes.Heatmap))
            {
                errors.Add(prefix + "display.ramp", "Only map and heatmap widgets have a scale.");
            }
        }

        // The base settings are accepted on every kind, so a chart-type change never fails on them (design D7 of add-small-sample-warnings).
        if (display.MinN is { } minN && (minN < 0 || minN > WidgetDisplay.MaxMinN))
        {
            errors.Add(prefix + "display.minN", "The small-base threshold must be a whole number from 0 (off) to 10,000.");
        }

        CheckLabels(display.Labels, prefix, errors);
        CheckContext(display, prefix, errors);
        CheckAnnotations(display, config, chartType, prefix, errors);
        CheckOrder(display, config, chartType, prefix, errors);
        CheckSecondary(display.Secondary, chartType, prefix, errors);
    }

    /// <summary>
    /// A widget's own order of values (design D6 of add-declared-category-order). The shape rules (the count, the text and the repeats of
    /// each value) run on every request; the rules that depend on the chart type, the group column, the series column and the sort are
    /// skipped when <paramref name="chartType"/> is null, as the other per-kind rules are. Values are not checked against the table: a
    /// script can change them later.
    /// </summary>
    private static void CheckOrder(WidgetDisplay display, WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (display.Order is not { } order)
        {
            return;
        }

        const string tooMany = "An order lists at most 100 values.";
        if (order.Group is { } groups)
        {
            var key = prefix + "display.order.group";
            CategoryOrder.CheckValues(groups, CategoryOrder.MaxValues, tooMany, key, errors);
            if (chartType is not null && groups.Count > 0)
            {
                if (chartType is ChartTypes.Stat or ChartTypes.Histogram or ChartTypes.Map)
                {
                    errors.Add(key, $"A {chartType} widget has no groups to order.");
                }
                else if (config.GroupBy is null)
                {
                    errors.Add(key, "Ordering groups needs a column to group by.");
                }
                else if (chartType is ChartTypes.Bar or ChartTypes.Column && config.Sort is not null)
                {
                    errors.Add(key, "A sorted widget takes the order of its groups from its sort: remove the sort or this order.");
                }
                else if (chartType == ChartTypes.Boxplot && config.Sort is { By: SortValues.ByValue })
                {
                    errors.Add(key, "A box plot ordered by median takes the order of its boxes from their medians: remove that order or this one.");
                }
            }
        }

        if (order.Series is { } series)
        {
            var key = prefix + "display.order.series";
            CategoryOrder.CheckValues(series, CategoryOrder.MaxValues, tooMany, key, errors);
            if (chartType is not null && series.Count > 0)
            {
                if (chartType == ChartTypes.Pyramid)
                {
                    errors.Add(key, "A population pyramid's sides are its left and right values.");
                }
                else if (chartType == ChartTypes.Likert)
                {
                    errors.Add(key, "A Likert widget's answers follow its scale.");
                }
                else if (config.SeriesBy is null || !ChartTypes.HasSeries(chartType))
                {
                    errors.Add(key, "Only a widget with a series has an order of series.");
                }
            }
        }
    }

    /// <summary>
    /// A widget's reference lines, line interpolation and data labels (design D2 of the chart annotations). The shape rules (the count,
    /// the kind, the value and the label of each line, the name of an interpolation) run on every request; the rules that depend on the
    /// chart type, the series and the layout are skipped when <paramref name="chartType"/> is null, as the other per-kind rules are.
    /// </summary>
    private static void CheckAnnotations(WidgetDisplay display, WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        var takesLines = chartType is null || chartType is ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line;
        var layout = config.SeriesBy is not null && chartType is ChartTypes.Bar or ChartTypes.Column ? config.Layout : null;
        if (display.ReferenceLines is { } lines)
        {
            var key = prefix + "display.referenceLines";
            if (lines.Count > WidgetDisplay.MaxReferenceLines)
            {
                errors.Add(key, $"A widget has at most {WidgetDisplay.MaxReferenceLines} reference lines.");
            }

            if (!takesLines)
            {
                errors.Add(key, "Only bar, column and line widgets have reference lines.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Count; i++)
            {
                CheckReferenceLine(lines[i], $"{key}[{i}]", takesLines && chartType is not null, config, layout, seen, errors);
            }
        }

        if (display.Interpolation is { } interpolation)
        {
            if (!LineInterpolations.IsKnown(interpolation))
            {
                errors.Add(prefix + "display.interpolation", $"The interpolation must be '{LineInterpolations.Linear}' or '{LineInterpolations.Step}'.");
            }
            else if (chartType is not null && chartType != ChartTypes.Line)
            {
                errors.Add(prefix + "display.interpolation", "Only a line chart has an interpolation.");
            }
        }

        if (display.DataLabels is { } dataLabels)
        {
            if (chartType is not null && chartType is not (ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line or ChartTypes.Pie))
            {
                errors.Add(prefix + "display.dataLabels", "Only bar, column, line and pie widgets have data labels.");
            }
            else if (dataLabels && chartType is not null && SeriesLayouts.Adds(layout))
            {
                errors.Add(prefix + "display.dataLabels", "Stacked bars have no data labels.");
            }
        }
    }

    /// <summary>One reference line: <paramref name="key"/> is <c>…referenceLines[i]</c>, and <paramref name="perKind"/> asks for the rules that need the chart type, the series and the layout.</summary>
    private static void CheckReferenceLine(
        WidgetReferenceLine? line, string key, bool perKind, WidgetConfig config, string? layout, HashSet<string> seen, FieldErrors errors)
    {
        if (line is null)
        {
            errors.Add(key, "Each reference line needs a kind.");
            return;
        }

        CheckReferenceLineLabel(line.Label, key + ".label", errors);
        if (!ReferenceLineKinds.IsKnown(line.Kind))
        {
            errors.Add(key + ".kind", $"The kind must be '{ReferenceLineKinds.Mean}', '{ReferenceLineKinds.Median}', '{ReferenceLineKinds.Overall}' or '{ReferenceLineKinds.Value}'.");
            return;
        }

        var kind = line.Kind!;
        if (kind != ReferenceLineKinds.Value && !seen.Add(kind))
        {
            errors.Add(key + ".kind", $"The {kind} line is listed twice.");
        }

        if (kind == ReferenceLineKinds.Value && line.Value is null)
        {
            errors.Add(key + ".value", "A 'value' line needs its value.");
        }
        else if (kind != ReferenceLineKinds.Value && line.Value is not null)
        {
            errors.Add(key + ".value", "Only a 'value' line takes a value.");
        }

        if (!perKind)
        {
            return;
        }

        if (kind is ReferenceLineKinds.Mean or ReferenceLineKinds.Median && config.SeriesBy is not null)
        {
            errors.Add(key + ".kind", "A mean or median line needs a chart with no series.");
        }
        else if (kind == ReferenceLineKinds.Overall && config.Ratio is null)
        {
            errors.Add(key + ".kind", "An overall line needs a ratio: over a measure it would be the sum of every bar.");
        }

        if (kind == ReferenceLineKinds.Value && line.Value is { } value && layout == SeriesLayouts.Percent && (value < 0 || value > 1))
        {
            errors.Add(key + ".value", "In a 100% layout a value is a share from 0 to 1.");
        }
    }

    // The length is that of the label once trimmed and the control characters are looked for in the label as sent, as CheckText does for
    // the widget's texts; the messages are those of a label.
    private static void CheckReferenceLineLabel(string? label, string key, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        if (label.Trim().Length > WidgetDisplay.MaxReferenceLineLabel)
        {
            errors.Add(key, $"A reference line's label has at most {WidgetDisplay.MaxReferenceLineLabel} characters.");
        }

        if (label.Any(char.IsControl))
        {
            errors.Add(key, "A reference line's label cannot contain line breaks or other control characters.");
        }
    }

    /// <summary>
    /// A widget's texts (design D2 of the widget context), on every kind and with no rule per kind: a subtitle, a footnote, a caption
    /// text and a unit within their lengths, counted after trimming as a title is, with no control character (a line break, a tab, NUL
    /// or U+0085 among them), and a caption that has its own text is not also hidden. The form turns line breaks into spaces before
    /// sending, so these rules only refuse an API or MCP client.
    /// </summary>
    private static void CheckContext(WidgetDisplay display, string prefix, FieldErrors errors)
    {
        CheckText(display.Subtitle, "subtitle", WidgetDisplay.MaxSubtitleLength, prefix + "display.subtitle", errors);
        CheckText(display.Footnote, "footnote", WidgetDisplay.MaxFootnoteLength, prefix + "display.footnote", errors);
        CheckText(display.Caption?.Text, "caption", WidgetDisplay.MaxCaptionLength, prefix + "display.caption.text", errors);
        CheckText(display.Unit, "unit", ScriptEndpoints.MaxUnitLength, prefix + "display.unit", errors);
        if (display.Caption is { Hidden: true } && !string.IsNullOrWhiteSpace(display.Caption.Text))
        {
            errors.Add(prefix + "display.caption", "Give the caption its own text or hide it, not both.");
        }
    }

    private static void CheckText(string? input, string name, int max, string key, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        // The length is that of the text once trimmed. The control characters are looked for in the text as sent: a line break at its end
        // is one too, and trimming it would quietly save a text the author did not mean to send.
        if (input.Trim().Length > max)
        {
            errors.Add(key, $"The {name} is longer than {max} characters.");
        }

        if (input.Any(char.IsControl))
        {
            errors.Add(key, $"The {name} cannot contain line breaks or other control characters.");
        }
    }

    /// <summary>
    /// A widget's own column labels (design D5 of the indicator column metadata): at most <see cref="ColumnLabels.MaxWidgetLabels"/>,
    /// each keyed by a column's name and a label of 1 to <see cref="ColumnLabels.MaxLength"/> characters with no control character. No
    /// rule per chart type, and none against the table: a label for a column the table lost does not make the widget stale.
    /// </summary>
    private static void CheckLabels(IReadOnlyDictionary<string, string>? labels, string prefix, FieldErrors errors)
    {
        if (labels is null)
        {
            return;
        }

        if (labels.Count > ColumnLabels.MaxWidgetLabels)
        {
            errors.Add(prefix + "display.labels", $"A widget labels at most {ColumnLabels.MaxWidgetLabels} columns.");
        }

        foreach (var (column, label) in labels)
        {
            if (ColumnLabels.CheckKey(column, prefix + "display.labels", errors))
            {
                ColumnLabels.CheckLabel(label, $"{prefix}display.labels.{column}", errors);
            }
        }
    }

    /// <summary>
    /// The dashboard filters a widget ignores, by column (design D6): at most 8, once each, each a published column's name. They are
    /// not checked against any dashboard's filters, so a move, a copy or a filter defined again keeps them.
    /// </summary>
    private static void CheckIgnoredDashboardFilters(IReadOnlyList<string> columns, string prefix, FieldErrors errors)
    {
        if (columns.Count > DashboardFilterRules.MaxFilters)
        {
            errors.Add(prefix + "ignoredDashboardFilters", $"A widget can ignore at most {DashboardFilterRules.MaxFilters} dashboard filters.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            var field = $"{prefix}ignoredDashboardFilters[{i}]";
            if (!ColumnNames.IsValidColumnName(columns[i]))
            {
                errors.Add(field, $"'{columns[i]}' is not a valid column name.");
            }
            else if (!seen.Add(columns[i]))
            {
                errors.Add(field, $"The filter on column '{columns[i]}' is already ignored.");
            }
        }
    }

    /// <summary>The checks of a map's tooltip columns that need no table: where they may be, how many, and the shape of each.</summary>
    private static void CheckTooltip(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        var items = config.Tooltip;
        if (items.Count == 0)
        {
            return;
        }

        if (chartType is not null && chartType != ChartTypes.Map)
        {
            errors.Add(prefix + "tooltip", "Only map widgets can have tooltip columns.");
            return;
        }

        if (items.Count > WidgetConfig.MaxTooltipItems)
        {
            errors.Add(prefix + "tooltip", $"A map has at most {WidgetConfig.MaxTooltipItems} tooltip columns.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var field = $"{prefix}tooltip[{i}]";
            if (items[i] is not { } item)
            {
                errors.Add(field, "A tooltip column needs a column.");
                continue;
            }

            if (string.IsNullOrEmpty(item.Column))
            {
                errors.Add(field + ".column", "Choose the tooltip column.");
            }
            else if (item.Column.Length > DataQueryBuilder.MaxTextLength)
            {
                errors.Add(field + ".column", $"The tooltip column is longer than {DataQueryBuilder.MaxTextLength} characters.");
            }
            else if (!seen.Add(item.Column))
            {
                errors.Add(field + ".column", $"Column '{item.Column}' is already a tooltip column.");
            }
            else if (item.Column == config.GeometryColumn)
            {
                errors.Add(field + ".column", "The geometry column cannot be a tooltip column.");
            }

            if (item.Label is { Length: > WidgetConfig.MaxTooltipLabel })
            {
                errors.Add(field + ".label", $"The label is longer than {WidgetConfig.MaxTooltipLabel} characters.");
            }

            if (item.Format is not null && !TooltipFormats.All.Contains(item.Format))
            {
                errors.Add(field + ".format", $"The format must be one of {string.Join(", ", TooltipFormats.All)}.");
            }
        }
    }

    /// <summary>
    /// The series column and what goes with it (design D2): which kinds may or must have one, its layout, a pyramid's sides and a Likert
    /// scale. Per-kind rules are skipped when <paramref name="chartType"/> is null, as the query endpoint may have none.
    /// </summary>
    private static void CheckSeries(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        CheckColumnName(config.SeriesBy, prefix + "seriesBy", "series column", errors);
        if (config.SeriesBy is not null && config.SeriesBy == config.GroupBy)
        {
            errors.Add(prefix + "seriesBy", "Choose a series column other than the group column.");
        }
        else if (config.SeriesBy is not null && config.GroupBy is null)
        {
            errors.Add(prefix + "seriesBy", "A series needs a column to group by.");
        }

        if (chartType is not null)
        {
            if (config.SeriesBy is not null && !ChartTypes.HasSeries(chartType))
            {
                errors.Add(prefix + "seriesBy", $"A {chartType} widget cannot have a series.");
            }
            else if (config.SeriesBy is null && ChartTypes.NeedsSeries(chartType))
            {
                errors.Add(prefix + "seriesBy", $"A {chartType} widget needs a series column.");
            }
        }

        if (config.Layout is { } layout)
        {
            if (!SeriesLayouts.All.Contains(layout))
            {
                errors.Add(prefix + "layout", $"The layout must be '{SeriesLayouts.Grouped}', '{SeriesLayouts.Stacked}' or '{SeriesLayouts.Percent}'.");
            }
            else if (chartType is not null && chartType is not (ChartTypes.Bar or ChartTypes.Column))
            {
                errors.Add(prefix + "layout", "Only bar and column widgets have a layout.");
            }
            else if (config.SeriesBy is null)
            {
                errors.Add(prefix + "layout", "A layout needs a series column.");
            }
            else if (SeriesLayouts.Adds(layout) && config.Ratio is not null)
            {
                errors.Add(prefix + "layout", "Stacked bars add their values, so they need a measure, not a ratio.");
            }
        }

        CheckPyramid(config.Pyramid, chartType, prefix, errors);
        CheckScale(config, chartType, prefix, errors);
    }

    private static void CheckPyramid(WidgetPyramid? pyramid, string? chartType, string prefix, FieldErrors errors)
    {
        if (chartType is not null && chartType != ChartTypes.Pyramid)
        {
            if (pyramid is not null)
            {
                errors.Add(prefix + "pyramid", "Only population pyramids have sides.");
            }

            return;
        }

        if (pyramid is null)
        {
            if (chartType == ChartTypes.Pyramid)
            {
                errors.Add(prefix + "pyramid", "A population pyramid needs a left and a right value.");
            }

            return;
        }

        CheckSeriesValue(pyramid.Left, prefix + "pyramid.left", "Choose the value drawn to the left.", errors);
        CheckSeriesValue(pyramid.Right, prefix + "pyramid.right", "Choose the value drawn to the right.", errors);
        if (!string.IsNullOrEmpty(pyramid.Left) && string.Equals(pyramid.Left, pyramid.Right, StringComparison.Ordinal))
        {
            errors.Add(prefix + "pyramid.right", "The left and right values must differ.");
        }
    }

    private static void CheckScale(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        var scale = config.Scale;
        if (chartType is not null && chartType != ChartTypes.Likert)
        {
            if (scale.Count > 0)
            {
                errors.Add(prefix + "scale", "Only Likert widgets have a scale.");
            }

            return;
        }

        if (chartType is null && scale.Count == 0)
        {
            return;
        }

        if (chartType == ChartTypes.Likert && config.Ratio is not null)
        {
            errors.Add(prefix + "measure", "A Likert widget shows shares of a measure, not a ratio.");
        }

        if (scale.Count < WidgetConfig.MinScaleValues || scale.Count > WidgetConfig.MaxScaleValues)
        {
            errors.Add(prefix + "scale", $"A Likert scale has between {WidgetConfig.MinScaleValues} and {WidgetConfig.MaxScaleValues} values.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var counts = new int[3];
        var lastRank = -1;
        for (var i = 0; i < scale.Count; i++)
        {
            var field = $"{prefix}scale[{i}]";
            if (scale[i] is not { } step)
            {
                errors.Add(field, "Each value of the scale needs a name and a side.");
                continue;
            }

            CheckSeriesValue(step.Value, field + ".value", "Name the value.", errors);
            if (!string.IsNullOrEmpty(step.Value) && !seen.Add(step.Value))
            {
                errors.Add(field + ".value", $"'{step.Value}' is already in the scale.");
            }

            var rank = LikertSides.Rank(step.Side);
            if (rank < 0)
            {
                errors.Add(field + ".side", $"The side must be '{LikertSides.Negative}', '{LikertSides.Neutral}' or '{LikertSides.Positive}'.");
                continue;
            }

            counts[rank]++;
            if (rank < lastRank)
            {
                errors.Add(field + ".side", "Negative values come first, then the neutral one, then the positive ones.");
            }

            lastRank = Math.Max(lastRank, rank);
        }

        if (counts[1] > 1)
        {
            errors.Add(prefix + "scale", "A scale has at most one neutral value.");
        }

        if (counts[0] > WidgetConfig.MaxScaleSide || counts[2] > WidgetConfig.MaxScaleSide)
        {
            errors.Add(prefix + "scale", $"A scale has at most {WidgetConfig.MaxScaleSide} values on each side.");
        }
    }

    /// <summary>A value of the series column named in a pyramid or a scale: text, not empty, as long as a filter's value may be.</summary>
    private static void CheckSeriesValue(string? value, string field, string emptyMessage, FieldErrors errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(field, emptyMessage);
        }
        else if (value.Length > DataQueryBuilder.MaxTextLength)
        {
            errors.Add(field, $"The value is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }
    }

    private static void CheckBasemap(string? basemap, string? chartType, string prefix, FieldErrors errors)
    {
        if (basemap is null)
        {
            return;
        }

        if (!BasemapKinds.All.Contains(basemap))
        {
            errors.Add(prefix + "basemap", $"The basemap must be '{BasemapKinds.Streets}' or '{BasemapKinds.Satellite}'.");
        }
        else if (chartType is not null && chartType != ChartTypes.Map)
        {
            errors.Add(prefix + "basemap", "Only map widgets can have a basemap.");
        }
    }

    /// <summary>The checks of a widget's order that need no table: where it may be, and the shape of its fields.</summary>
    private static void CheckSort(WidgetConfig config, string? chartType, string prefix, FieldErrors errors)
    {
        if (config.Sort is not { } sort)
        {
            return;
        }

        if (chartType is not null && chartType is not (ChartTypes.Bar or ChartTypes.Column or ChartTypes.Boxplot))
        {
            errors.Add(prefix + "sort", "Only bar, column and box plot widgets can be sorted.");
            return;
        }

        if (config.GroupBy is null)
        {
            errors.Add(prefix + "sort", "Sorting needs a column to group by.");
        }

        if (sort.By is not (SortValues.ByValue or SortValues.ByColumn))
        {
            errors.Add(prefix + "sort.by", "Sort by 'value' or by 'column'.");
        }
        else if (sort.By == SortValues.ByColumn && chartType == ChartTypes.Boxplot)
        {
            errors.Add(prefix + "sort.by", "A box plot can only be ordered by its median ('value').");
        }

        if (sort.Direction is not (SortValues.Asc or SortValues.Desc))
        {
            errors.Add(prefix + "sort.direction", "The direction must be 'asc' or 'desc'.");
        }

        if (sort.By == SortValues.ByColumn && sort.Column is null)
        {
            errors.Add(prefix + "sort.column", "Choose a column to sort by.");
        }
        else if (sort.By == SortValues.ByValue && sort.Column is not null)
        {
            errors.Add(prefix + "sort.column", "Sort by the plotted value or by a column, not both.");
        }

        CheckColumnName(sort.Column, prefix + "sort.column", "sort column", errors);

        if (sort.CompareAs is not null && sort.CompareAs is not (SortValues.Text or SortValues.Number))
        {
            errors.Add(prefix + "sort.compareAs", "Compare as 'text' or as 'number'.");
        }
        else if (sort.By == SortValues.ByValue && sort.CompareAs is not null)
        {
            errors.Add(prefix + "sort.compareAs", "The plotted value is always compared as a number.");
        }
    }

    /// <summary>
    /// What is wrong with comparing <paramref name="column"/> the way <paramref name="sort"/> says: a date, timestamp or true/false
    /// column is only compared in its own order. Null when the order fits the column.
    /// </summary>
    public static string? SortColumnError(WidgetSort sort, TableColumn column) =>
        sort.By == SortValues.ByColumn && sort.CompareAs is not null && column.Type is DataColumnType.Date or DataColumnType.Timestamp or DataColumnType.Boolean
            ? $"Column '{column.Name}' is {DataQueryBuilder.Describe(column.Type)}, so it is sorted in its own order and cannot be compared as text or as a number."
            : null;

    private static void CheckColumnName(string? name, string field, string what, FieldErrors errors)
    {
        if (name is { Length: > DataQueryBuilder.MaxTextLength })
        {
            errors.Add(field, $"The {what} is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }
    }

    /// <summary>What a filter must have whatever the table is. The meaning of the operator for the column's type is checked against the table.</summary>
    private static void CheckFilterShape(IReadOnlyList<DataFilter> filters, string prefix, FieldErrors errors)
    {
        if (filters.Count > DataQueryBuilder.MaxFilters)
        {
            errors.Add(prefix + "filters", $"At most {DataQueryBuilder.MaxFilters} filters are allowed.");
            return;
        }

        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var field = $"{prefix}filters[{i}]";
            if (filter is null || string.IsNullOrEmpty(filter.Column))
            {
                errors.Add(field, "A filter needs a column.");
            }
            else if (!FilterOps.Contains(filter.Op))
            {
                errors.Add(field, $"The filter operator must be one of {string.Join(", ", FilterOps)}.");
            }
            else if (filter.Values is { Count: > DataQueryBuilder.MaxInValues })
            {
                errors.Add(field, $"Column '{filter.Column}': at most {DataQueryBuilder.MaxInValues} values are allowed.");
            }
        }
    }

    /// <summary>
    /// The checks of a query's dashboard filters, which need no table (design D3): at most 8, one per column, each a published column's
    /// name, a known kind and a value of that kind. Errors are keyed <c>dashboardFilters[i]</c> and name the column.
    /// </summary>
    public static void CheckDashboardFilters(IReadOnlyList<AppliedDashboardFilter>? filters, FieldErrors errors)
    {
        if (filters is null)
        {
            return;
        }

        if (filters.Count > DashboardFilterRules.MaxFilters)
        {
            errors.Add("dashboardFilters", $"At most {DashboardFilterRules.MaxFilters} dashboard filters are allowed.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < filters.Count; i++)
        {
            var field = $"dashboardFilters[{i}]";
            if (filters[i] is not { } filter || !ColumnNames.IsValidColumnName(filter.Column))
            {
                errors.Add(field, $"The dashboard filter's column '{filters[i]?.Column}' is not a valid column name.");
            }
            else if (!seen.Add(filter.Column!))
            {
                errors.Add(field, $"Column '{filter.Column}' has more than one dashboard filter.");
            }
            else if (!DashboardFilterKinds.IsKnown(filter.Kind))
            {
                errors.Add(field, $"Column '{filter.Column}': the kind must be one of {string.Join(", ", DashboardFilterKinds.All)}.");
            }
            else
            {
                DashboardFilterKinds.CheckValue(filter.Kind!, filter.Value, filter.Column!, field, errors);
            }
        }
    }

    /// <summary>
    /// Checks a query against the published table: the measure or ratio columns must exist and be numbers, the group and geometry
    /// columns must exist, and the filters must make sense for their columns' types, exactly as the clean data dashboard checks them.
    /// </summary>
    public static void CheckAgainstColumns(WidgetConfig config, IReadOnlyList<TableColumn> columns, string timeZone, string prefix, FieldErrors errors)
    {
        var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        void Numeric(string? name, string field)
        {
            if (name is null)
            {
                return;
            }

            if (!byName.TryGetValue(name, out var column))
            {
                errors.Add(field, $"The table has no column '{name}'.");
            }
            else if (!IsNumeric(column))
            {
                errors.Add(field, $"Column '{name}' is not a number, so it cannot be plotted.");
            }
        }

        Numeric(config.Measure, prefix + "measure");
        Numeric(config.Ratio?.Numerator, prefix + "ratio.numerator");
        Numeric(config.Ratio?.Denominator, prefix + "ratio.denominator");
        if (config.GroupBy is not null && !byName.ContainsKey(config.GroupBy))
        {
            errors.Add(prefix + "groupBy", $"The table has no column '{config.GroupBy}'.");
        }

        if (config.SeriesBy is not null && !byName.ContainsKey(config.SeriesBy))
        {
            errors.Add(prefix + "seriesBy", $"The table has no column '{config.SeriesBy}'.");
        }

        if (config.GeometryColumn is not null && !byName.ContainsKey(config.GeometryColumn))
        {
            errors.Add(prefix + "geometryColumn", $"The table has no column '{config.GeometryColumn}'.");
        }

        if (config.Sort is { By: SortValues.ByColumn, Column: { } sortColumn } sort)
        {
            if (!byName.TryGetValue(sortColumn, out var column))
            {
                errors.Add(prefix + "sort.column", $"The table has no column '{sortColumn}'.");
            }
            else if (SortColumnError(sort, column) is { } sortError)
            {
                errors.Add(prefix + "sort.column", sortError);
            }
        }

        for (var i = 0; i < config.Tooltip.Count; i++)
        {
            if (config.Tooltip[i] is not { Column: { Length: > 0 } tooltipColumn } item)
            {
                continue;
            }

            var field = $"{prefix}tooltip[{i}]";
            if (!byName.TryGetValue(tooltipColumn, out var column))
            {
                errors.Add(field + ".column", $"The table has no column '{tooltipColumn}'.");
            }
            else if (item.Format is not null && !IsNumeric(column))
            {
                errors.Add(field + ".format", "Only a number column can have a format.");
            }
        }

        var filterErrors = new FieldErrors();
        DataQueryBuilder.Build(new DataQueryRequest(config.Filters), columns, timeZone, filterErrors);
        foreach (var (field, messages) in filterErrors.ToDictionary())
        {
            foreach (var message in messages)
            {
                errors.Add(prefix + field, message);
            }
        }
    }

    public static bool IsNumeric(TableColumn column) => column.Type is DataColumnType.Integer or DataColumnType.Decimal;
}
