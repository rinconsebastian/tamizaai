using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Indicators;

/// <summary>What a dashboard version lists as changed (design D2), stored as strings in <c>dashboard_versions.changes</c>.</summary>
public static class DashboardChangeKinds
{
    /// <summary>The state before version history began, which the migration recorded as version 1 (design D5).</summary>
    public const string Initial = "initial";

    /// <summary>The dashboard was added, alone or with its project.</summary>
    public const string Created = "created";

    /// <summary>The dashboard is a copy of the one <see cref="DashboardChange.Dashboard"/> names; noted on the copy only.</summary>
    public const string Duplicated = "duplicated";

    /// <summary>The dashboard took the name <see cref="DashboardChange.Dashboard"/> holds.</summary>
    public const string Renamed = "renamed";

    public const string FiltersChanged = "filtersChanged";

    /// <summary>The dashboard's palette or colors kept by value changed.</summary>
    public const string ColorsChanged = "colorsChanged";

    public const string WidgetAdded = "widgetAdded";

    /// <summary>A widget's title, chart type or query changed, with or without its layout.</summary>
    public const string WidgetChanged = "widgetChanged";

    public const string WidgetRemoved = "widgetRemoved";

    /// <summary>A widget went to the dashboard <see cref="DashboardChange.Dashboard"/> names.</summary>
    public const string WidgetMovedOut = "widgetMovedOut";

    /// <summary>A widget came from the dashboard <see cref="DashboardChange.Dashboard"/> names.</summary>
    public const string WidgetMovedIn = "widgetMovedIn";

    /// <summary>Widgets were moved or resized on the dashboard, and nothing else of them changed.</summary>
    public const string LayoutChanged = "layoutChanged";

    /// <summary>A restore brought back the version <see cref="DashboardChange.Version"/> names (design D7).</summary>
    public const string Restored = "restored";

    /// <summary>A state no version held, which a restore recorded before replacing it (design D7).</summary>
    public const string Unrecorded = "unrecorded";

    public static readonly IReadOnlyList<string> All =
        [Initial, Created, Duplicated, Renamed, FiltersChanged, ColorsChanged, WidgetAdded, WidgetChanged, WidgetRemoved, WidgetMovedOut,
         WidgetMovedIn, LayoutChanged, Restored, Unrecorded];

    /// <summary>The kinds of a version that stays as it was recorded: no later write joins it (design D4).</summary>
    public static bool Closes(string? kind) => kind is Initial or Unrecorded or Restored;
}

/// <summary>
/// One entry of what a version holds (design D2). Names and titles are kept as they were at the time, so the history says what things
/// were called then; they are member text, never markup. The fields a kind does not use are left out of the JSON.
/// </summary>
/// <param name="Kind">One of <see cref="DashboardChangeKinds"/>.</param>
/// <param name="Widget">The widget's title as it is after the change, or before it for a removal.</param>
/// <param name="Dashboard">The new name for a rename, the original's for a copy, the other dashboard's for a widget move.</param>
/// <param name="Version">The number of the version a restore brought back.</param>
public sealed record DashboardChange(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Widget = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Dashboard = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Version = null);

/// <summary>A dashboard and the number of its current version, as a flush leaves it.</summary>
public sealed record DashboardVersionNumber(Guid DashboardId, int Version);

/// <summary>
/// Records the versions of a project's own dashboards (design D3). It lives for one request: each dashboard handler notes what it
/// changed once its write has committed, and the caller flushes once, after its handler calls. The dashboards route group flushes after
/// its handler with origin <c>app</c> (<see cref="RecordDashboardVersions"/>); an MCP dashboard write tool flushes with origin
/// <c>mcp</c> and its <c>change</c> in a <c>finally</c>, so what an earlier call committed is recorded when a later one is refused. The
/// flush records one version of each dashboard noted, so one request is one version of each dashboard it changed. A restore records its
/// version itself (<see cref="RestoreAsync"/>), since it must name the version restored and never grows (design D7).
/// </summary>
public sealed class DashboardVersions(
    TamizaDbContext db, TimeProvider time, IOptions<IndicatorDashboardOptions> options, ILogger<DashboardVersions> logger)
{
    /// <summary>The versions a dashboard keeps, the current one included (design D6). A product rule, not a setting.</summary>
    public const int Kept = 10;

    /// <summary>The most entries a version lists; the changes after them only count in <c>more_changes</c> (design D2).</summary>
    public const int MaxChanges = 20;

    /// <summary>
    /// How long after its last write a version made through MCP still takes the next write of the same request (design D4). A product
    /// rule, as <see cref="Kept"/> is.
    /// </summary>
    public static readonly TimeSpan RequestWindow = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The changes noted since the last flush, per dashboard, in the order they were noted.</summary>
    private readonly Dictionary<Guid, (Guid ProjectId, List<DashboardChange> Changes)> _noted = [];

    /// <summary>Whether a change was noted since the last flush.</summary>
    public bool Pending => _noted.Count > 0;

    /// <summary>
    /// The dashboards a change was noted for since the last flush. The flush answers the version of each, or leaves one out when it is
    /// gone or its version could not be recorded (design D3), so an MCP result never names a version that lacks the change (design D10).
    /// </summary>
    public IReadOnlyCollection<Guid> Noted => [.. _noted.Keys];

    /// <summary>Notes that a write which has committed changed the dashboard; the next flush records it.</summary>
    public void Note(Guid projectId, Guid dashboardId, DashboardChange change)
    {
        if (!_noted.TryGetValue(dashboardId, out var noted))
        {
            noted = (projectId, []);
            _noted[dashboardId] = noted;
        }

        noted.Changes.Add(change);
    }

    /// <summary>
    /// Records one version of each dashboard noted since the last flush, in id order, each in its own transaction under the project's
    /// lock (design D3), and answers each dashboard's current version. A dashboard removed meanwhile is skipped: its versions went with
    /// it. The write each version records has already committed, so the flush runs with no cancellation: a page closed right after a
    /// drag, or a client that cancels its call, does not drop the version. It never throws: a dashboard whose version could not be
    /// recorded is logged and left out of the answer, and the next change records the dashboard as it then is.
    /// </summary>
    /// <param name="origin">Where the changes were made.</param>
    /// <param name="authorId">Who made them.</param>
    /// <param name="note">An AI client's request (its <c>change</c>), for origin <c>mcp</c>; ignored for origin <c>app</c>.</param>
    public async Task<IReadOnlyList<DashboardVersionNumber>> FlushAsync(DashboardVersionOrigin origin, Guid authorId, string? note = null)
    {
        if (_noted.Count == 0)
        {
            return [];
        }

        var noted = _noted.OrderBy(entry => entry.Key).ToList();
        _noted.Clear();
        var request = origin == DashboardVersionOrigin.Mcp ? note?.Trim() : null;
        var numbers = new List<DashboardVersionNumber>();
        foreach (var (dashboardId, (projectId, changes)) in noted)
        {
            try
            {
                if (await RecordAsync(projectId, dashboardId, changes, origin, authorId, request) is { } number)
                {
                    numbers.Add(new DashboardVersionNumber(dashboardId, number));
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "A version of dashboard {DashboardId} of project {ProjectId} could not be recorded; the change itself is saved.",
                    dashboardId, projectId);
            }
            finally
            {
                // A failed save leaves its entities tracked: the next dashboard's transaction must not try them again.
                db.ChangeTracker.Clear();
            }
        }

        return numbers;
    }

    /// <summary>The steps of design D3 for one dashboard: lock, build, compare, grow or insert, prune, commit. Null when it is gone.</summary>
    private async Task<int?> RecordAsync(
        Guid projectId, Guid dashboardId, IReadOnlyList<DashboardChange> changes, DashboardVersionOrigin origin, Guid authorId, string? note)
    {
        var ct = CancellationToken.None;
        // The handlers' entities are theirs: a refused write may have left some unsaved, and the flush saves only versions.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await DashboardEndpoints.LockProjectAsync(db, projectId, ct);
        var content = await ContentAsync(db, projectId, dashboardId, ct);
        if (content is null)
        {
            return null;
        }

        // jsonb equality in PostgreSQL: key order and spacing aside, the same name, filters, palette, colors kept by value and widgets in the same order.
        var newest = await db.DashboardVersions
            .Where(v => v.ProjectId == projectId && v.DashboardId == dashboardId)
            .OrderByDescending(v => v.Number)
            .Select(v => new { Version = v, Same = v.Content == content })
            .FirstOrDefaultAsync(ct);
        var now = DashboardEndpoints.Now(time);
        if (newest is not null && Grows(newest.Version, origin, authorId, note, now))
        {
            // The same request again, or a change a concurrent flush of it has already recorded, which only adds its entries.
            var version = newest.Version;
            var (entries, more) = Append(ReadChanges(version.Changes), version.MoreChanges, changes);
            if (!newest.Same)
            {
                version.Content = content;
            }

            version.Changes = JsonSerializer.Serialize(entries, Json);
            version.MoreChanges = more;
            version.SavedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return version.Number;
        }

        if (newest is { Same: true })
        {
            // A save that changed nothing, or a change a concurrent flush has already recorded.
            return newest.Version.Number;
        }

        var number = (newest?.Version.Number ?? 0) + 1;
        var (listed, left) = Append([], 0, changes);
        db.DashboardVersions.Add(new DashboardVersion
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            DashboardId = dashboardId,
            Number = number,
            Content = content,
            Changes = JsonSerializer.Serialize(listed, Json),
            MoreChanges = left,
            Origin = origin,
            Note = note,
            AuthorId = authorId,
            CreatedAt = now,
            SavedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await PruneAsync(db, projectId, dashboardId, number, ct);
        await transaction.CommitAsync(ct);
        return number;
    }

    /// <summary>
    /// Deletes the versions that the newest, numbered <paramref name="newest"/>, pushes out: the 10 newest stay, this one included, and
    /// numbers keep counting up (design D6).
    /// </summary>
    private static Task PruneAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, int newest, CancellationToken ct) =>
        Of(db, projectId, dashboardId).Where(v => v.Number <= newest - Kept).ExecuteDeleteAsync(ct);

    /// <summary>
    /// Whether a write made at <paramref name="now"/> joins the newest version instead of making a new one (design D4). Only a write
    /// through MCP does, by the newest version's author, when that version was made through MCP for the same request (trimmed, whatever
    /// its case) and last written less than <see cref="RequestWindow"/> ago. Every change made in Tamiza is a version of its own, and
    /// the state before history, a state no version held and a restore never grow.
    /// </summary>
    public static bool Grows(DashboardVersion newest, DashboardVersionOrigin origin, Guid authorId, string? note, DateTimeOffset now) =>
        origin == DashboardVersionOrigin.Mcp
        && newest.Origin == DashboardVersionOrigin.Mcp
        && newest.RestoredFrom is null
        && !ReadChanges(newest.Changes).Any(change => DashboardChangeKinds.Closes(change.Kind))
        && newest.AuthorId == authorId
        && SameRequest(newest.Note, note)
        && now - newest.SavedAt < RequestWindow;

    /// <summary>Two requests of an AI client are the same when their texts are, trimmed and whatever their case.</summary>
    public static bool SameRequest(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A version's entries once <paramref name="added"/> are appended (design D2): at most <see cref="MaxChanges"/> are listed and the
    /// others only counted, and an entry equal to the last one listed is not listed again, so moving widgets several times in a row
    /// lists one layout change.
    /// </summary>
    public static (IReadOnlyList<DashboardChange> Changes, int MoreChanges) Append(
        IReadOnlyList<DashboardChange> changes, int moreChanges, IEnumerable<DashboardChange> added)
    {
        var listed = changes.ToList();
        var more = moreChanges;
        foreach (var change in added)
        {
            if (listed.Count > 0 && listed[^1] == change)
            {
                continue;
            }

            if (listed.Count >= MaxChanges)
            {
                more++;
            }
            else
            {
                listed.Add(change);
            }
        }

        return (listed, more);
    }

    /// <summary>A version's entries as stored in <c>changes</c>.</summary>
    public static List<DashboardChange> ReadChanges(string json) => JsonSerializer.Deserialize<List<DashboardChange>>(json, Json) ?? [];

    /// <summary>
    /// The dashboard as a version holds it (design D2): its name, its filters, its palette and colors kept by value when they are not
    /// the defaults, and its widgets in reading order, each with its id, title, chart type, config and layout. Null when the project has
    /// no such dashboard.
    /// </summary>
    public static async Task<string?> ContentAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, CancellationToken ct)
    {
        var dashboard = await db.Dashboards.AsNoTracking()
            .Where(d => d.Id == dashboardId && d.ProjectId == projectId)
            .Select(d => new { d.Name, d.Filters, d.Palette, d.ValueColors })
            .SingleOrDefaultAsync(ct);
        if (dashboard is null)
        {
            return null;
        }

        var widgets = await db.DashboardWidgets.AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.DashboardId == dashboardId)
            .ToListAsync(ct);
        return Content(dashboard.Name, dashboard.Filters, dashboard.Palette, dashboard.ValueColors, widgets);
    }

    /// <summary>
    /// The content of a dashboard with this name, these filters, this palette, these colors kept by value and these widgets (design D2).
    /// The widgets are in reading order, top to bottom, then left to right, then the older first, then by id, so the same dashboard
    /// always gives the same list. The filters, the colors kept by value, each config and each layout are embedded as stored, never read
    /// and written again through <see cref="WidgetConfig"/>: a version is exactly what was saved, a restore writes back the same JSON,
    /// and the migration's version 1 is what this builds (design D5).
    /// </summary>
    /// <remarks>
    /// <c>palette</c> and <c>valueColors</c> follow the filters and are written only when they are not the defaults, <see cref="Palettes.Tamiza"/>
    /// and an empty list (design D6 of dashboard palettes). A dashboard that never changed its colors has the content it had before
    /// palettes existed, byte for byte, which the newest version of every dashboard migrated from script 0018 holds: the flush and a
    /// restore, which compare contents, find them equal and record no version for the new shape alone.
    /// </remarks>
    public static string Content(string name, string filters, string palette, string valueColors, IEnumerable<DashboardWidget> widgets)
    {
        var ordered = widgets
            .Select(widget => (Widget: widget, Layout: DashboardEndpoints.ReadLayout(widget.Layout)))
            .OrderBy(w => w.Layout.Y).ThenBy(w => w.Layout.X).ThenBy(w => w.Widget.CreatedAt).ThenBy(w => w.Widget.Id)
            .Select(w => (JsonNode?)new JsonObject
            {
                ["id"] = w.Widget.Id,
                ["title"] = w.Widget.Title,
                ["chartType"] = w.Widget.ChartType,
                ["config"] = JsonNode.Parse(w.Widget.Config),
                ["layout"] = JsonNode.Parse(w.Widget.Layout),
            })
            .ToArray();
        var content = new JsonObject
        {
            ["name"] = name,
            ["filters"] = JsonNode.Parse(filters),
        };
        if (palette != Palettes.Tamiza)
        {
            content["palette"] = palette;
        }

        if (JsonNode.Parse(valueColors) is JsonArray { Count: > 0 } colors)
        {
            content["valueColors"] = colors;
        }

        content["widgets"] = new JsonArray(ordered);
        return content.ToJsonString();
    }

    /// <summary>The dashboard's kept versions, scoped by project as every read of them is (design D1).</summary>
    private static IQueryable<DashboardVersion> Of(TamizaDbContext db, Guid projectId, Guid dashboardId) =>
        db.DashboardVersions.Where(v => v.ProjectId == projectId && v.DashboardId == dashboardId);

    /// <summary>The number of the dashboard's current version, its newest; null while it has none (design D5).</summary>
    public static Task<int?> CurrentAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, CancellationToken ct) =>
        Of(db, projectId, dashboardId).AsNoTracking().MaxAsync(v => (int?)v.Number, ct);

    /// <summary>The dashboard's kept versions, newest first, as its history lists them (design D9). The caller checks that it exists.</summary>
    public static async Task<DashboardVersionList> ListAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, CancellationToken ct)
    {
        var rows = await Of(db, projectId, dashboardId).AsNoTracking()
            .OrderByDescending(v => v.Number)
            .Select(v => new { Version = v, Author = new Person(v.Author!.Id, v.Author.Name) })
            .ToListAsync(ct);
        var current = rows.Count > 0 ? rows[0].Version.Number : 0;
        return new DashboardVersionList(current, rows.Select(row => Summary(row.Version, row.Author, row.Version.Number == current)).ToList());
    }

    /// <summary>One kept version as the history lists it; null when the dashboard does not keep it.</summary>
    public static async Task<DashboardVersionSummary?> SummaryAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, int number, CancellationToken ct)
    {
        var row = await Of(db, projectId, dashboardId).AsNoTracking()
            .Where(v => v.Number == number)
            .Select(v => new { Version = v, Author = new Person(v.Author!.Id, v.Author.Name) })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : Summary(row.Version, row.Author, await CurrentAsync(db, projectId, dashboardId, ct) == number);
    }

    /// <summary>
    /// A kept version's name, filters, palette, colors kept by value and widgets, with what the history shows of it (design D9); null
    /// when the dashboard does not keep it. The filters, the colors and each widget's config and layout are read as
    /// <see cref="DashboardEndpoints.GetDashboardAsync"/> reads the dashboard's, so a config saved before later fields existed reads
    /// with their defaults, and a version recorded before palettes existed reads as the palette <see cref="Palettes.Tamiza"/> with no
    /// colors kept.
    /// </summary>
    public static async Task<DashboardVersionDetails?> DetailsAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, int number, CancellationToken ct)
    {
        var row = await Of(db, projectId, dashboardId).AsNoTracking()
            .Where(v => v.Number == number)
            .Select(v => new { Version = v, Author = new Person(v.Author!.Id, v.Author.Name) })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var version = row.Version;
        var content = ReadContent(version.Content);
        return new DashboardVersionDetails(
            version.Number,
            await CurrentAsync(db, projectId, dashboardId, ct) == number,
            content.Name,
            DashboardEndpoints.ReadFilters(content.Filters),
            content.Palette,
            DashboardEndpoints.ReadValueColors(content.ValueColors),
            content.Widgets
                .Select(w => new VersionWidget(w.Id, w.Title, w.ChartType, DashboardEndpoints.ReadConfig(w.Config), DashboardEndpoints.ReadLayout(w.Layout)))
                .ToList(),
            ReadChanges(version.Changes),
            version.MoreChanges,
            version.RestoredFrom,
            version.Origin,
            version.Note,
            row.Author,
            version.CreatedAt,
            version.SavedAt);
    }

    private static DashboardVersionSummary Summary(DashboardVersion version, Person author, bool current)
    {
        var content = ReadContent(version.Content);
        return new DashboardVersionSummary(
            version.Number, current, content.Name, content.Widgets.Count, DashboardEndpoints.ReadFilters(content.Filters).Count,
            ReadChanges(version.Changes), version.MoreChanges, version.RestoredFrom, version.Origin, version.Note, author, version.CreatedAt,
            version.SavedAt);
    }

    /// <summary>
    /// A version's content as <see cref="Content"/> built it (design D2), with the filters, the colors kept by value and each widget's
    /// config and layout as the JSON text stored, so a restore writes them back as they were saved. What a content lacks reads as empty,
    /// and a missing palette as <see cref="Palettes.Tamiza"/>: a version recorded before palettes existed has neither key (design D6 of
    /// dashboard palettes). A palette the catalog no longer has reads as <see cref="Palettes.Tamiza"/> too, since the dashboard's
    /// <c>ck_dashboards_palette</c> check would refuse it on a restore.
    /// </summary>
    public static VersionContent ReadContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString()! : "";
        var filters = root.TryGetProperty("filters", out var listed) && listed.ValueKind == JsonValueKind.Array ? listed.GetRawText() : "[]";
        var palette = root.TryGetProperty("palette", out var painted) && painted.ValueKind == JsonValueKind.String
            && Palettes.IsKnown(painted.GetString()) ? painted.GetString()! : Palettes.Tamiza;
        var colors = root.TryGetProperty("valueColors", out var kept) && kept.ValueKind == JsonValueKind.Array ? kept.GetRawText() : "[]";
        var widgets = root.TryGetProperty("widgets", out var held) && held.ValueKind == JsonValueKind.Array
            ? held.EnumerateArray()
                .Select(w => new VersionContentWidget(
                    w.GetProperty("id").GetGuid(),
                    w.GetProperty("title").GetString()!,
                    w.GetProperty("chartType").GetString()!,
                    w.GetProperty("config").GetRawText(),
                    w.GetProperty("layout").GetRawText()))
                .ToList()
            : [];
        return new VersionContent(name, filters, palette, colors, widgets);
    }

    /// <summary>
    /// Version 1 of the dashboard a new project starts with, saved with the project and the dashboard (design D3): what it holds, noted
    /// as added, by the project's creator.
    /// </summary>
    public static DashboardVersion FirstVersion(Dashboard dashboard) => new()
    {
        Id = Guid.CreateVersion7(dashboard.CreatedAt),
        ProjectId = dashboard.ProjectId,
        DashboardId = dashboard.Id,
        Number = 1,
        Content = Content(dashboard.Name, dashboard.Filters, dashboard.Palette, dashboard.ValueColors, []),
        Changes = JsonSerializer.Serialize(new[] { new DashboardChange(DashboardChangeKinds.Created) }, Json),
        Origin = DashboardVersionOrigin.App,
        AuthorId = dashboard.CreatedBy,
        CreatedAt = dashboard.CreatedAt,
        SavedAt = dashboard.CreatedAt,
    };

    // Restoring a version (design D7)

    /// <summary>
    /// Restores a kept version of the dashboard (design D7): its name, filters, palette, colors kept by value and widgets become the
    /// version's, recorded as a new version that names the version restored. It runs in one transaction under the project's lock, and
    /// records first a state of the dashboard that no version holds, so the restore never erases a change from the history. The
    /// dashboard keeps its id, its place and its scheduled email. It answers 201 with the new version, 200 with the current one when the dashboard already is as the restore would
    /// leave it, and the refusals of the REST route otherwise: 404 <see cref="DashboardEndpoints.NotFoundCode"/> or
    /// <see cref="DashboardEndpoints.VersionNotFoundCode"/>, or 400 on <c>widgets</c> for a version over the widget limit.
    /// </summary>
    /// <param name="write">The page and the REST API restore with <see cref="DashboardRestoreWrite.App"/>; an AI client with its request and guard.</param>
    /// <param name="authorId">Who restores: the new version's author, and the author of the widgets it adds again.</param>
    /// <exception cref="DashboardChangedException">
    /// A guarded restore found a current version other than the one its writer read, or recorded a state no version held; nothing was
    /// restored, and a version that state needed is kept.
    /// </exception>
    public async Task<Results<Created<DashboardRestore>, Ok<DashboardRestore>, ValidationProblem, ProblemHttpResult>> RestoreAsync(
        Guid projectId, Guid dashboardId, int number, DashboardRestoreWrite write, Guid authorId, CancellationToken ct)
    {
        var keepName = false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RestoreOnceAsync(projectId, dashboardId, number, write, authorId, keepName, ct);
            }
            catch (DbUpdateException exception) when (
                attempt < 3 && (DashboardEndpoints.IsNameTaken(exception) || exception is DbUpdateConcurrencyException))
            {
                // Renames take no lock, so another dashboard may take the version's name after the check: the unique index refuses the
                // save, and the restore runs again keeping the current name, as an add answers a name taken meanwhile (design D7 step 5).
                // A save that finds a row gone runs again too, on the rows as they then are; the restore locks the dashboard's rows
                // before it reads them, so a widget removed meanwhile is gone before the reads or waits for the restore's commit.
                keepName |= DashboardEndpoints.IsNameTaken(exception);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>The ten steps of design D7, once. <paramref name="keepName"/> keeps the current name whatever another dashboard is named.</summary>
    private async Task<Results<Created<DashboardRestore>, Ok<DashboardRestore>, ValidationProblem, ProblemHttpResult>> RestoreOnceAsync(
        Guid projectId, Guid dashboardId, int number, DashboardRestoreWrite write, Guid authorId, bool keepName, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await DashboardEndpoints.LockProjectAsync(db, projectId, ct);
        // A rename, a filters save, a colors save, a widget's change that stays on the dashboard and a widget's removal take no project
        // lock, so they could commit between the reads below and the save, and be overwritten or mixed into the restored state with no
        // version holding them. Locking the dashboard's row and its widgets' rows first makes each of them either commit before the reads,
        // so step 2 records it, or wait for this commit and land after the restore, where its own flush records it (design D7).
        // Those writers lock one row in a statement of their own and take the project lock only later, in their flush, so they
        // never hold what the restore waits for.
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM tamiza.dashboards WHERE id = {dashboardId} AND project_id = {projectId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM tamiza.dashboard_widgets WHERE dashboard_id = {dashboardId} AND project_id = {projectId} FOR UPDATE", ct);

        // 1. Checks.
        var dashboard = await db.Dashboards.SingleOrDefaultAsync(d => d.Id == dashboardId && d.ProjectId == projectId, ct);
        if (dashboard is null)
        {
            return DashboardEndpoints.DashboardNotFound();
        }

        var stored = await Of(db, projectId, dashboardId).AsNoTracking().Where(v => v.Number == number).Select(v => v.Content).SingleOrDefaultAsync(ct);
        if (stored is null)
        {
            return DashboardEndpoints.VersionNotFound();
        }

        var version = ReadContent(stored);
        // Only possible after an operator lowered the limit, as for a duplicate.
        var max = options.Value.MaxWidgetsPerDashboard;
        if (version.Widgets.Count > max)
        {
            return Problems.Validation("widgets", DashboardEndpoints.WidgetLimitMessage(max));
        }

        // 2. A state no version holds: a change that committed without the lock just before, its flush still to come, or one whose flush
        // failed. That writer's flush would find the restored content and record nothing, so it is recorded here first.
        var widgets = await db.DashboardWidgets.Where(w => w.ProjectId == projectId && w.DashboardId == dashboardId).ToListAsync(ct);
        var current = Content(dashboard.Name, dashboard.Filters, dashboard.Palette, dashboard.ValueColors, widgets);
        var newest = await Of(db, projectId, dashboardId).AsNoTracking()
            .OrderByDescending(v => v.Number)
            .Select(v => new { v.Number, Same = v.Content == current })
            .FirstOrDefaultAsync(ct);
        var latest = newest?.Number ?? 0;
        var unrecorded = newest is not { Same: true };
        if (unrecorded)
        {
            // Attributed as the state before history is (design D5): to the last edit recorded on the dashboard and its widgets.
            var (editor, editedAt) = LastEdit(dashboard, widgets);
            latest++;
            db.DashboardVersions.Add(NewVersion(projectId, dashboardId, latest, current, [new DashboardChange(DashboardChangeKinds.Unrecorded)],
                null, DashboardVersionOrigin.App, null, editor, editedAt));
            await db.SaveChangesAsync(ct);
            await PruneAsync(db, projectId, dashboardId, latest, ct);
        }

        // 3. The guard of an AI client, which acts on what it read possibly minutes earlier (design D10). A version step 2 recorded is
        // kept all the same.
        if (write.GuardsBase && (unrecorded || latest != write.BaseVersion))
        {
            var changed = await ChangedAsync(projectId, dashboardId, latest, ct);
            await transaction.CommitAsync(ct);
            throw changed;
        }

        // 4. Nothing to do: the version as the restore would write it, with the name it would give the dashboard and the copies it would
        // keep (step 7), is the dashboard as it is, which the newest version holds. So restoring again a version whose name was kept, or
        // whose widget came back as a copy, changes nothing.
        var nameKept = version.Name != dashboard.Name
            && (keepName || await DashboardEndpoints.NameTakenAsync(db, projectId, version.Name, except: dashboardId, ct) is not null);
        var name = nameKept ? dashboard.Name : version.Name;
        var ids = version.Widgets.Select(w => w.Id).ToList();
        var elsewhere = await db.DashboardWidgets.AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.DashboardId != dashboardId && ids.Contains(w.Id))
            .Join(db.Dashboards, w => w.DashboardId, d => d.Id, (w, d) => new { w.Id, DashboardId = d.Id, d.Name })
            .ToDictionaryAsync(w => w.Id, ct);
        var kept = KeptCopies(version.Widgets.Where(w => elsewhere.ContainsKey(w.Id)), widgets.Where(w => !ids.Contains(w.Id)));
        var written = Written(stored, name, kept);
        if (await Of(db, projectId, dashboardId).Where(v => v.Number == latest).Select(v => v.Content == written).SingleAsync(ct))
        {
            var unchanged = await AnswerAsync(projectId, dashboardId, restored: false, latest, nameKept, [], ct);
            await transaction.CommitAsync(ct);
            return TypedResults.Ok(unchanged);
        }

        // 5. Name, kept when another dashboard of the project now has the version's, ignoring case. 6. Filters, palette and colors kept by
        // value: a version recorded before palettes existed reads as the palette tamiza with none kept (design D6 of dashboard palettes).
        if (name != dashboard.Name)
        {
            dashboard.Name = name;
            dashboard.NormalizedName = DashboardValidation.Normalize(name);
        }

        dashboard.Filters = version.Filters;
        dashboard.Palette = version.Palette;
        dashboard.ValueColors = version.ValueColors;

        // 7. Widgets, with the JSON the version holds. The published tables are not checked: the config was valid when saved, and a
        // widget whose indicator or columns are gone shows its message, as after a move.
        var now = DashboardEndpoints.Now(time);
        var onDashboard = widgets.ToDictionary(w => w.Id);
        var copies = new List<RestoredCopy>();
        foreach (var saved in version.Widgets)
        {
            if (onDashboard.TryGetValue(saved.Id, out var widget))
            {
                // It keeps who created it and when.
                WriteBack(widget, saved, authorId, now);
            }
            else if (elsewhere.TryGetValue(saved.Id, out var holder))
            {
                // The other dashboard keeps it as it is: this one gets a copy, the one it already has when an earlier restore made it.
                if (kept.TryGetValue(saved.Id, out var copy))
                {
                    WriteBack(copy, saved, authorId, now);
                }
                else
                {
                    copy = NewWidget(Guid.CreateVersion7(now), projectId, dashboardId, saved, authorId, now);
                    db.DashboardWidgets.Add(copy);
                }

                copies.Add(new RestoredCopy(saved.Id, copy.Id, saved.Title, holder.DashboardId, holder.Name));
            }
            else
            {
                db.DashboardWidgets.Add(NewWidget(saved.Id, projectId, dashboardId, saved, authorId, now));
            }
        }

        var keptIds = kept.Values.Select(w => w.Id).ToHashSet();
        db.DashboardWidgets.RemoveRange(widgets.Where(w => !ids.Contains(w.Id) && !keptIds.Contains(w.Id)));

        // 8. The dashboard's last change.
        dashboard.UpdatedBy = authorId;
        dashboard.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // 9. The new version, built again after the writes since copies have new ids. A restore never grows and no later write joins it
        // (design D4), so it can always be undone on its own.
        var restored = await ContentAsync(db, projectId, dashboardId, ct);
        latest++;
        var note = write.Origin == DashboardVersionOrigin.Mcp ? write.Note?.Trim() : null;
        db.DashboardVersions.Add(NewVersion(projectId, dashboardId, latest, restored!,
            [new DashboardChange(DashboardChangeKinds.Restored, Version: number)], number, write.Origin, note, authorId, now));
        await db.SaveChangesAsync(ct);
        await PruneAsync(db, projectId, dashboardId, latest, ct);

        // 10. The answer.
        var answer = await AnswerAsync(projectId, dashboardId, restored: true, latest, nameKept, copies, ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/dashboards/{dashboardId}/versions/{latest}", answer);
    }

    /// <summary>
    /// What a restore answers, read in its transaction under the lock, where the dashboard and its version <paramref name="latest"/>
    /// exist: that version, now the current one, and the dashboard as it then is.
    /// </summary>
    private async Task<DashboardRestore> AnswerAsync(
        Guid projectId, Guid dashboardId, bool restored, int latest, bool nameKept, IReadOnlyList<RestoredCopy> copies, CancellationToken ct) =>
        new(restored,
            (await SummaryAsync(db, projectId, dashboardId, latest, ct))!,
            nameKept,
            copies,
            (await DashboardEndpoints.ViewAsync(db, projectId, dashboardId, ct))!);

    /// <summary>The refusal of a guarded restore, naming the dashboard's current version (design D10).</summary>
    private async Task<DashboardChangedException> ChangedAsync(Guid projectId, Guid dashboardId, int latest, CancellationToken ct)
    {
        var newest = await Of(db, projectId, dashboardId).AsNoTracking()
            .Where(v => v.Number == latest)
            .Select(v => new { Author = v.Author!.Name, v.SavedAt, v.Origin })
            .SingleAsync(ct);
        return new DashboardChangedException(latest, newest.Author, newest.SavedAt, newest.Origin);
    }

    /// <summary>The latest of the dashboard's and its widgets' last edits: who and when, the dashboard's on a tie.</summary>
    private static (Guid By, DateTimeOffset At) LastEdit(Dashboard dashboard, IEnumerable<DashboardWidget> widgets) =>
        new[] { (dashboard.UpdatedBy, dashboard.UpdatedAt) }.Concat(widgets.Select(w => (w.UpdatedBy, w.UpdatedAt))).MaxBy(edit => edit.UpdatedAt);

    /// <summary>
    /// A version's content as the restore would write it: with the name it gives the dashboard, which is the current one when it keeps
    /// it, and each widget it keeps a copy of under that copy's id. The version's own text when it changes neither.
    /// </summary>
    private static string Written(string content, string name, IReadOnlyDictionary<Guid, DashboardWidget> kept)
    {
        var node = JsonNode.Parse(content)!.AsObject();
        if (node["name"]?.GetValue<string>() == name && kept.Count == 0)
        {
            return content;
        }

        node["name"] = name;
        if (node["widgets"] is JsonArray widgets)
        {
            foreach (var widget in widgets.OfType<JsonObject>())
            {
                if (widget["id"]?.GetValue<string>() is { } id && kept.TryGetValue(Guid.Parse(id), out var copy))
                {
                    widget["id"] = copy.Id;
                }
            }
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// The copies a restore keeps (design D7 steps 4 and 7). For each of <paramref name="moved"/>, the version's widgets that another
    /// dashboard now holds, a widget of <paramref name="others"/>, those of this dashboard that the version does not hold, with the same
    /// title, chart type, config and layout, such as the copy an earlier restore of the version made. A widget stands for one of the
    /// version's at most, so two of its widgets that are the same never claim the same copy; the older is taken first.
    /// </summary>
    private static Dictionary<Guid, DashboardWidget> KeptCopies(IEnumerable<VersionContentWidget> moved, IEnumerable<DashboardWidget> others)
    {
        var free = others.OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).ToList();
        var kept = new Dictionary<Guid, DashboardWidget>();
        foreach (var saved in moved)
        {
            var copy = free.Find(w => w.Title == saved.Title && w.ChartType == saved.ChartType
                && JsonNode.DeepEquals(JsonNode.Parse(w.Config), JsonNode.Parse(saved.Config))
                && JsonNode.DeepEquals(JsonNode.Parse(w.Layout), JsonNode.Parse(saved.Layout)));
            if (copy is not null)
            {
                kept[saved.Id] = copy;
                free.Remove(copy);
            }
        }

        return kept;
    }

    /// <summary>Writes a version's widget back over the widget with its id, where they differ.</summary>
    private static void WriteBack(DashboardWidget widget, VersionContentWidget saved, Guid authorId, DateTimeOffset now)
    {
        var differs = false;
        if (widget.Title != saved.Title)
        {
            widget.Title = saved.Title;
            differs = true;
        }

        if (widget.ChartType != saved.ChartType)
        {
            widget.ChartType = saved.ChartType;
            differs = true;
        }

        if (!JsonNode.DeepEquals(JsonNode.Parse(widget.Config), JsonNode.Parse(saved.Config)))
        {
            widget.Config = saved.Config;
            differs = true;
        }

        if (!JsonNode.DeepEquals(JsonNode.Parse(widget.Layout), JsonNode.Parse(saved.Layout)))
        {
            widget.Layout = saved.Layout;
            differs = true;
        }

        if (differs)
        {
            widget.UpdatedBy = authorId;
            widget.UpdatedAt = now;
        }
    }

    private static DashboardWidget NewWidget(Guid id, Guid projectId, Guid dashboardId, VersionContentWidget saved, Guid authorId, DateTimeOffset now) => new()
    {
        Id = id,
        ProjectId = projectId,
        DashboardId = dashboardId,
        ChartType = saved.ChartType,
        Title = saved.Title,
        Config = saved.Config,
        Layout = saved.Layout,
        CreatedBy = authorId,
        CreatedAt = now,
        UpdatedBy = authorId,
        UpdatedAt = now,
    };

    private static DashboardVersion NewVersion(
        Guid projectId, Guid dashboardId, int number, string content, IReadOnlyList<DashboardChange> changes, int? restoredFrom,
        DashboardVersionOrigin origin, string? note, Guid authorId, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        ProjectId = projectId,
        DashboardId = dashboardId,
        Number = number,
        Content = content,
        Changes = JsonSerializer.Serialize(changes, Json),
        RestoredFrom = restoredFrom,
        Origin = origin,
        Note = note,
        AuthorId = authorId,
        CreatedAt = at,
        SavedAt = at,
    };
}

/// <summary>
/// A version's content as <see cref="DashboardVersions.ReadContent"/> reads it: the filters and the colors kept by value as their JSON
/// text, and the palette, which is <see cref="Palettes.Tamiza"/> when the content has none.
/// </summary>
public sealed record VersionContent(string Name, string Filters, string Palette, string ValueColors, IReadOnlyList<VersionContentWidget> Widgets);

/// <summary>A widget of a version's content, its config and layout as their JSON text.</summary>
public sealed record VersionContentWidget(Guid Id, string Title, string ChartType, string Config, string Layout);

/// <summary>Where a restore comes from and, for an AI client, which version it read as the current one (design D7 step 3, D10).</summary>
/// <param name="Note">The AI client's request (its <c>change</c>), kept trimmed with the version; ignored for origin <c>app</c>.</param>
/// <param name="BaseVersion">The number of the version the writer read as the dashboard's current one.</param>
/// <param name="GuardsBase">Whether <paramref name="BaseVersion"/> must still be the current version for the restore to go ahead.</param>
public sealed record DashboardRestoreWrite(DashboardVersionOrigin Origin, string? Note, int? BaseVersion, bool GuardsBase)
{
    /// <summary>The dashboard page and the REST API: no guard, the last change wins, and the state replaced is a version (design D7).</summary>
    public static readonly DashboardRestoreWrite App = new(DashboardVersionOrigin.App, null, null, false);

    /// <summary>An AI client, with its request and the current version it read; refused with <see cref="DashboardChangedException"/> when another is current.</summary>
    public static DashboardRestoreWrite Mcp(string change, int baseVersion) => new(DashboardVersionOrigin.Mcp, change, baseVersion, true);
}

/// <summary>
/// A guarded restore found that the dashboard's current version is not the one the writer read (design D7 step 3), as
/// <c>ScriptChangedException</c> is for scripts. It names the current version: its number, author, the time of its last change and
/// where it was made.
/// </summary>
public sealed class DashboardChangedException(int current, string author, DateTimeOffset savedAt, DashboardVersionOrigin origin)
    : Exception("The dashboard was changed since it was read.")
{
    public int Current { get; } = current;

    public string Author { get; } = author;

    public DateTimeOffset SavedAt { get; } = savedAt;

    public DashboardVersionOrigin Origin { get; } = origin;
}

/// <summary>
/// Records, once the handler of a request to the dashboards route group has run, a version of each dashboard it changed, made in
/// Tamiza by the caller (design D3). Each request runs one handler, which notes only after its own commit, so a refused change and a
/// read have noted nothing and record nothing. The handler's answer is the request's, whatever the flush meets.
/// </summary>
internal sealed class RecordDashboardVersions(DashboardVersions versions, ICurrentUser user) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        finally
        {
            if (versions.Pending)
            {
                await versions.FlushAsync(DashboardVersionOrigin.App, user.Id);
            }
        }
    }
}
