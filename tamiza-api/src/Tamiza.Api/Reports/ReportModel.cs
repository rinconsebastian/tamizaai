using Tamiza.Api.Indicators;

namespace Tamiza.Api.Reports;

/// <summary>What became of one widget of a scheduled email.</summary>
public enum WidgetOutcome
{
    /// <summary>It has rows: a stat value or a table.</summary>
    Shown,

    /// <summary>It was read, and no row matches its query.</summary>
    NoRows,

    NotPublished,
    MissingColumns,

    /// <summary>The query was refused: the table changed under the widget's settings, such as a measure published as text.</summary>
    NoLongerFits,

    TimedOut,
}

/// <summary>A column of a widget's table. <paramref name="ExcelFormat"/> is the number format of its cells in the workbook; null for text.</summary>
/// <param name="Unit">The indicator's unit, written in the workbook's header since its cells hold bare numbers.</param>
public sealed record ReportColumn(string Header, string? ExcelFormat = null, string? Unit = null)
{
    public bool Numeric => ExcelFormat is not null;
}

/// <summary>One cell: the number or text the workbook holds (null for no value), and the text the bodies show.</summary>
public sealed record ReportCell(object? Value, string Text);

/// <param name="Position">1 for the first widget in the dashboard's reading order.</param>
/// <param name="Caption">What the widget plots: the indicator's label, with the measure or the ratio's two columns when it names them.</param>
/// <param name="StatValue">A stat tile's value as the email shows it.</param>
/// <param name="Reason">Why the widget shows no table or value, in the email's language; null when it has one.</param>
/// <param name="Notes">What the reader should know about the rows shown: the row limit, or filters that do not narrow it.</param>
/// <param name="SeriesColumns">A cross table's series columns, which come between the group column and the "Total" column; 0 for any other table.</param>
public sealed record ReportWidget(
    int Position,
    string Title,
    string ChartType,
    string Caption,
    WidgetOutcome Outcome,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<ReportCell>> Rows,
    string? StatValue,
    string? Reason,
    IReadOnlyList<string> Notes,
    int SeriesColumns = 0)
{
    /// <summary>The most series columns the body of an email shows for one widget; the workbook shows them all.</summary>
    public const int BodySeriesColumns = 6;

    public bool IsStat => ChartType == ChartTypes.Stat;

    /// <summary>The series columns the body leaves out; 0 for a table that fits.</summary>
    public int HiddenSeriesColumns => Math.Max(0, SeriesColumns - BodySeriesColumns);

    /// <summary>
    /// The columns of the body's table, by index: all of them, or for a cross table of more than <see cref="BodySeriesColumns"/> series
    /// the group column, the first series and the "Total" column.
    /// </summary>
    public IReadOnlyList<int> BodyColumns => HiddenSeriesColumns == 0
        ? [.. Enumerable.Range(0, Columns.Count)]
        : [0, .. Enumerable.Range(1, BodySeriesColumns), Columns.Count - 1];

    /// <summary>Not published, a missing column, settings that no longer fit, or a timeout: the widget could not be read.</summary>
    public bool Unreadable => Outcome is WidgetOutcome.NotPublished or WidgetOutcome.MissingColumns or WidgetOutcome.NoLongerFits or WidgetOutcome.TimedOut;
}

/// <summary>A dashboard filter in force: its label and its default, as the filter bar writes it.</summary>
public sealed record ReportFilterLine(string Label, string Value);

/// <summary>
/// Everything a scheduled email says, computed once and written three ways (HTML, plain text and the workbook), renderer-neutral
/// (design D2).
/// </summary>
/// <param name="DataPublishedAt">The latest time one of the widgets' indicators was published; null when none has.</param>
/// <param name="DashboardAddress">The dashboard's address in the deployment, for members.</param>
public sealed record ReportModel(
    Guid ProjectId,
    string ProjectName,
    Guid DashboardId,
    string DashboardName,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? DataPublishedAt,
    IReadOnlyList<ReportFilterLine> Filters,
    IReadOnlyList<ReportWidget> Widgets,
    string DashboardAddress,
    ReportFormat Format,
    ReportText Text)
{
    /// <summary>No widget, or none that could be read: the email is not sent (a widget with no matching rows was read).</summary>
    public bool NothingToSend => Widgets.Count == 0 || Widgets.All(w => w.Unreadable);

    public IEnumerable<ReportWidget> Stats => Widgets.Where(w => w.IsStat);

    public IEnumerable<ReportWidget> Tables => Widgets.Where(w => !w.IsStat);

    public string Subject => Text.Format("subject", ProjectName, DashboardName, Format.Day(Format.LocalDay(GeneratedAt)));
}
