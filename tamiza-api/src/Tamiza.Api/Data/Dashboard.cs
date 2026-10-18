namespace Tamiza.Api.Data;

/// <summary>
/// One of a project's own indicator dashboards, shown as a tab after Performance and Clean data. A project has 1 to 7, in
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

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
