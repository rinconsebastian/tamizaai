namespace Tamiza.Api.Data;

/// <summary>
/// One version of a <see cref="Dashboard"/>: the dashboard as it was after a change, with what changed (design D1). A dashboard keeps its
/// 10 newest versions, numbered 1, 2, 3... and never reused; they are removed with it. The newest is the dashboard as it is.
/// </summary>
public sealed class DashboardVersion
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>The <see cref="Dashboard"/> the version is of, always one of <see cref="ProjectId"/>'s.</summary>
    public Guid DashboardId { get; set; }

    /// <summary>1, 2, 3... per dashboard; unique in it.</summary>
    public int Number { get; set; }

    /// <summary>
    /// The dashboard's name, filters, palette, colors kept by value and widgets, as JSON text in a <c>jsonb</c> column (design D2): the
    /// widgets in reading order, and the filters, the colors kept by value, each widget's config and its layout embedded as stored. The
    /// palette and the colors are written only when they are not the defaults, so a version of a dashboard that never changed them has
    /// neither key (design D6 of dashboard palettes). The indicators module builds and reads it.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>What the version holds, as a JSON list of <c>{ kind, widget?, dashboard?, version? }</c> in a <c>jsonb</c> column; at most 20 entries.</summary>
    public string Changes { get; set; } = "[]";

    /// <summary>The changes left out of <see cref="Changes"/> once it holds 20 entries.</summary>
    public int MoreChanges { get; set; }

    /// <summary>The number of the version a restore brought back; null for any other change.</summary>
    public int? RestoredFrom { get; set; }

    /// <summary>Where the version was made: the dashboard page and the REST API (<c>app</c>) or an AI client (<c>mcp</c>).</summary>
    public DashboardVersionOrigin Origin { get; set; } = DashboardVersionOrigin.App;

    /// <summary>The request the AI client gave as <c>change</c>, 1 to 120 characters; null for a version made in Tamiza.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Who made the version. The state before version history began, and a state a restore found in no version, name the last editor
    /// recorded on the dashboard and its widgets (design D5, D7).
    /// </summary>
    public Guid AuthorId { get; set; }

    /// <summary>The first write the version holds.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The last write the version holds: the time the history shows.</summary>
    public DateTimeOffset SavedAt { get; set; }

    public User? Author { get; set; }
}

/// <summary>Where a dashboard version was made: <c>app</c> is the dashboard page and the REST API, <c>mcp</c> an AI client.</summary>
public enum DashboardVersionOrigin
{
    App,
    Mcp,
}
