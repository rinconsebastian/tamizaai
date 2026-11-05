namespace Tamiza.Api.Data;

/// <summary>
/// One of a project's own indicator dashboards, shown as a tab after Performance and Clean data. A project has 1 to 10, in
/// <see cref="Position"/> order, and each holds its <see cref="DashboardWidget"/>s.
/// </summary>
public sealed class Dashboard
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>The name as the member typed it, trimmed.</summary>
    public required string Name { get; set; }

    /// <summary>Trimmed and lower-cased; unique in the project.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>0 to n - 1 among the project's dashboards, renumbered on every add, duplicate, removal and reorder.</summary>
    public int Position { get; set; }

    /// <summary>The dashboard's filters, in order, as JSON text in a <c>jsonb</c> column: the indicators module validates and reads them.</summary>
    public string Filters { get; set; } = "[]";

    /// <summary>The id of the dashboard's palette, one of a fixed set the <c>ck_dashboards_palette</c> check lists; the indicators module validates and reads it.</summary>
    public string Palette { get; set; } = "tamiza";

    /// <summary>The colors kept for values of columns, in the order they were saved, as JSON text in a <c>jsonb</c> column: the indicators module validates and reads them.</summary>
    public string ValueColors { get; set; } = "[]";

    /// <summary>Where the dashboard's data comes from, 1 to 300 characters as the <c>ck_dashboards_source</c> check allows, or null for none; the indicators module validates and reads it.</summary>
    public string? Source { get; set; }

    /// <summary>The small-base threshold, 1 to 10,000 as the <c>ck_dashboards_min_n</c> check allows, or null for off; the indicators module validates and reads it.</summary>
    public int? MinN { get; set; }

    /// <summary>Whether each widget's base n shows under it.</summary>
    public bool ShowN { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
