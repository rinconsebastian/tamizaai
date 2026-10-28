using System.Globalization;
using Tamiza.Api.Indicators;
using Tamiza.Api.Reports;

namespace Tamiza.Api.UnitTests.Reports;

/// <summary>Report models built by hand, for the writers' tests.</summary>
internal static class ReportSamples
{
    public static readonly DateTimeOffset Generated = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    public static ReportModel Model(IReadOnlyList<ReportWidget> widgets, string locale = "en-US", IReadOnlyList<ReportFilterLine>? filters = null,
        string project = "Census 2026", string dashboard = "Housing")
    {
        var text = ReportText.For(locale);
        return new ReportModel(
            Guid.Parse("00000000-0000-0000-0000-0000000000aa"), project, Guid.Parse("00000000-0000-0000-0000-0000000000d1"), dashboard,
            Generated, Generated.AddHours(-7), filters ?? [new ReportFilterLine("Round", "3")], widgets,
            "https://tamiza.test/projects/aa/dashboards/d1", new ReportFormat(text.Culture, ReportFormat.ZoneOf("America/Bogota")), text);
    }

    public static ReportWidget Stat(int position, string title, string value) =>
        new(position, title, ChartTypes.Stat, $"{title}: value", WidgetOutcome.Shown, [], [], value, null, []);

    public static ReportWidget Bars(int position, string title, int rows, string format = "count", string? unit = null) =>
        new(position, title, ChartTypes.Bar, "Households visited: value", WidgetOutcome.Shown,
            [new ReportColumn("municipality"), new ReportColumn("Households visited: value", ReportDataBuilder.ExcelFormat(format), unit)],
            Enumerable.Range(1, rows).Select(i => (IReadOnlyList<ReportCell>)
                [new ReportCell($"Municipality {i}", $"Municipality {i}"), new ReportCell((double)i, i.ToString(CultureInfo.InvariantCulture))]).ToList(),
            null, null, []);

    public static ReportWidget Unreadable(int position, string title, string reason) =>
        new(position, title, ChartTypes.Bar, "Visits: value", WidgetOutcome.NotPublished, [], [], null, reason, []);
}
