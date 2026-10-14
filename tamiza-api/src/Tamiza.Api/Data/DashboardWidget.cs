namespace Tamiza.Api.Data;

/// <summary>
/// One widget of a project's indicators dashboard. <see cref="Config"/> (the widget's query) and <see cref="Layout"/> (its place
/// on the grid) are JSON text in <c>jsonb</c> columns: the indicators module validates and reads them.
/// </summary>
public sealed class DashboardWidget
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary><c>stat</c>, <c>bar</c>, <c>column</c>, <c>line</c>, <c>histogram</c>, <c>pie</c> or <c>map</c>.</summary>
    public required string ChartType { get; set; }

    public required string Title { get; set; }

    public required string Config { get; set; }

    public required string Layout { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
