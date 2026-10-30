using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Indicators;

/// <summary>
/// A project's own dashboards, 1 to <see cref="IndicatorDashboardOptions.MaxDashboards"/> in an order, each a canvas of widgets over the
/// tables the indicator scripts published (design D4). A route with a <c>dashboardId</c> answers 404 <see cref="NotFoundCode"/> when the
/// project has no such dashboard, whatever else it names. Every change to the project's set or order of dashboards, or to the number
/// of widgets of one of them, runs under one lock per project (design D3).
/// </summary>
public static class DashboardEndpoints
{
    public const string NotFoundCode = "dashboards.notFound";
    public const string LimitCode = "dashboards.limit";
    public const string LastOneCode = "dashboards.lastOne";
    public const string OrderChangedCode = "dashboards.orderChanged";

    /// <summary>The name of the dashboard every project starts with, as the migration gave the projects that existed before (design D7).</summary>
    public const string FirstName = "Indicators";

    /// <summary>The unique index that keeps one name per project whatever its case.</summary>
    private const string NameIndex = "ix_dashboards_project_id_normalized_name";

    /// <summary>The error on <c>dashboardId</c> when a widget is moved to a dashboard the project does not have.</summary>
    private const string MissingTargetMessage = "The dashboard does not exist.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        var dashboards = api.MapGroup("/projects/{projectId:guid}/dashboards").WithTags("Dashboards");
        dashboards.MapPost("", AddAsync).RequireProjectRole(ProjectRole.Analyst).WithName("AddDashboard");
        // "order" never reads as a dashboard: the id segments only match a guid.
        dashboards.MapPut("/order", ReorderAsync).RequireProjectRole(ProjectRole.Analyst).WithName("ReorderDashboards");
        dashboards.MapGet("/{dashboardId:guid}", GetDashboardAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetDashboard");
        dashboards.MapPatch("/{dashboardId:guid}", RenameAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RenameDashboard");
        dashboards.MapDelete("/{dashboardId:guid}", RemoveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveDashboard");
        dashboards.MapPost("/{dashboardId:guid}/copies", DuplicateAsync).RequireProjectRole(ProjectRole.Analyst).WithName("DuplicateDashboard");
        dashboards.MapPut("/{dashboardId:guid}/filters", SaveFiltersAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveDashboardFilters");
        dashboards.MapGet("/{dashboardId:guid}/values", GetValuesAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetDashboardColumnValues");
        dashboards.MapPost("/{dashboardId:guid}/widgets", CreateWidgetAsync)
            .RequireProjectRole(ProjectRole.Analyst).WithName("CreateDashboardWidget");
        dashboards.MapPatch("/{dashboardId:guid}/widgets/{widgetId:guid}", UpdateWidgetAsync)
            .RequireProjectRole(ProjectRole.Analyst).WithName("UpdateDashboardWidget");
        dashboards.MapDelete("/{dashboardId:guid}/widgets/{widgetId:guid}", DeleteWidgetAsync)
            .RequireProjectRole(ProjectRole.Analyst).WithName("DeleteDashboardWidget");
        return api;
    }

    /// <summary>The dashboard's name, its filters and its widgets in reading order, listed whether or not their indicator is published.</summary>
    internal static async Task<Results<Ok<DashboardView>, ProblemHttpResult>> GetDashboardAsync(
        Guid projectId, Guid dashboardId, TamizaDbContext db, CancellationToken ct)
    {
        var dashboard = await db.Dashboards.AsNoTracking()
            .Where(d => d.Id == dashboardId && d.ProjectId == projectId)
            .Select(d => new { d.Name, d.Filters })
            .SingleOrDefaultAsync(ct);
        if (dashboard is null)
        {
            return DashboardNotFound();
        }

        var widgets = await ReadWidgetsAsync(db, dashboardId, ct);
        return TypedResults.Ok(new DashboardView(dashboardId, dashboard.Name, ReadFilters(dashboard.Filters), widgets));
    }

    /// <summary>A dashboard's widgets in reading order: top to bottom, then left to right, then the older first.</summary>
    internal static async Task<List<Widget>> ReadWidgetsAsync(TamizaDbContext db, Guid dashboardId, CancellationToken ct) =>
        (await db.DashboardWidgets.AsNoTracking().Where(w => w.DashboardId == dashboardId).ToListAsync(ct))
            .Select(w => (Widget: ToWidget(w), w.CreatedAt))
            .OrderBy(w => w.Widget.Layout.Y).ThenBy(w => w.Widget.Layout.X).ThenBy(w => w.CreatedAt)
            .Select(w => w.Widget)
            .ToList();

    /// <summary>
    /// The most frequent values of a text column across the indicator tables of the dashboard's widgets, optionally only those that
    /// contain <c>search</c>, for a text filter (design D5). Empty when no table has the column as text.
    /// </summary>
    private static async Task<Results<Ok<DataValueList>, ValidationProblem, ProblemHttpResult>> GetValuesAsync(
        Guid projectId, Guid dashboardId, string? column, string? search, IndicatorDashboard indicators, TamizaDbContext db, CancellationToken ct)
    {
        if (!await DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardNotFound();
        }

        if (!Pipeline.ColumnNames.IsValidColumnName(column))
        {
            return Problems.Validation("column", "The column must be a published column's name.");
        }

        if (search is { Length: > DataQueryBuilder.MaxTextLength })
        {
            return Problems.Validation("search", $"The search text is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }

        var configs = await db.DashboardWidgets.AsNoTracking().Where(w => w.DashboardId == dashboardId).ToListAsync(ct);
        // Saved configs name valid indicators; the check keeps it so whatever a stored row holds.
        var names = configs.Select(w => ReadConfig(w).Indicator).Where(Pipeline.ColumnNames.IsValidTableName).Distinct(StringComparer.Ordinal).ToList();
        try
        {
            return TypedResults.Ok(new DataValueList(await indicators.ValuesAsync(projectId, names, column!, search, ct)));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    /// <summary>
    /// Replaces the dashboard's filters with the list sent, in its order (design D4). No lock: the last save wins. A dashboard removed
    /// since the check updates no row.
    /// </summary>
    internal static async Task<Results<Ok<DashboardFilterList>, ValidationProblem, ProblemHttpResult>> SaveFiltersAsync(
        Guid projectId, Guid dashboardId, DashboardFiltersRequest? request, TamizaDbContext db, ICurrentUser user, TimeProvider time,
        CancellationToken ct)
    {
        if (!await DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardNotFound();
        }

        var errors = new FieldErrors();
        var filters = DashboardValidation.Filters(request?.Filters, errors);
        if (filters is null)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var json = JsonSerializer.Serialize(filters, Json);
        var now = Now(time);
        var updated = await db.Dashboards
            .Where(d => d.Id == dashboardId && d.ProjectId == projectId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.Filters, json)
                .SetProperty(d => d.UpdatedBy, user.Id)
                .SetProperty(d => d.UpdatedAt, now), ct);
        return updated == 0 ? DashboardNotFound() : TypedResults.Ok(new DashboardFilterList(filters));
    }

    /// <summary>Adds a dashboard with no widgets after the project's others.</summary>
    internal static async Task<Results<Created<DashboardEntry>, ValidationProblem, ProblemHttpResult>> AddAsync(
        Guid projectId, DashboardNameRequest? request, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var (name, error) = DashboardValidation.Name(request?.Name);
        if (error is not null)
        {
            return Problems.Validation("name", error);
        }

        // Counting and inserting are one step per project: two adds at 6 dashboards give one 7th (design D3).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockProjectAsync(db, projectId, ct);
        var dashboards = await OrderedAsync(db, projectId, ct);
        if (dashboards.Count >= IndicatorDashboardOptions.MaxDashboards)
        {
            return Limit();
        }

        if (await NameTakenAsync(db, projectId, name!, except: Guid.Empty, ct) is { } taken)
        {
            return taken;
        }

        var dashboard = NewDashboard(projectId, name!, dashboards.Count, user, Now(time));
        db.Dashboards.Add(dashboard);
        dashboards.Add(dashboard);
        Renumber(dashboards);
        if (await CommitNamedAsync(db, transaction, projectId, name!, ct) is { } raced)
        {
            return raced;
        }

        return TypedResults.Created($"/api/v1/projects/{projectId}/dashboards/{dashboard.Id}", ToEntry(dashboard));
    }

    /// <summary>Renames the dashboard. Its widgets, its place and its address stay the same.</summary>
    internal static async Task<Results<Ok<DashboardEntry>, ValidationProblem, ProblemHttpResult>> RenameAsync(
        Guid projectId, Guid dashboardId, DashboardNameRequest? request, TamizaDbContext db, ICurrentUser user, TimeProvider time,
        CancellationToken ct)
    {
        if (!await DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardNotFound();
        }

        var (name, error) = DashboardValidation.Name(request?.Name);
        if (error is not null)
        {
            return Problems.Validation("name", error);
        }

        // The dashboard's own name in another case is not taken.
        if (await NameTakenAsync(db, projectId, name!, except: dashboardId, ct) is { } taken)
        {
            return taken;
        }

        // No lock: the last rename wins. A dashboard removed since the check updates no row (design D3).
        var normalized = DashboardValidation.Normalize(name!);
        var now = Now(time);
        int updated;
        try
        {
            updated = await db.Dashboards
                .Where(d => d.Id == dashboardId && d.ProjectId == projectId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(d => d.Name, name!)
                    .SetProperty(d => d.NormalizedName, normalized)
                    .SetProperty(d => d.UpdatedBy, user.Id)
                    .SetProperty(d => d.UpdatedAt, now), ct);
        }
        catch (Exception exception) when (IsNameTaken(exception))
        {
            return await NameTakenAsync(db, projectId, name!, except: dashboardId, ct) ?? NameTaken(name!);
        }

        return updated == 0 ? DashboardNotFound() : TypedResults.Ok(new DashboardEntry(dashboardId, name!));
    }

    /// <summary>Removes the dashboard with its widgets. The project's only dashboard stays.</summary>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid projectId, Guid dashboardId, TamizaDbContext db, CancellationToken ct)
    {
        // Under the lock two removals at 2 dashboards leave one, and no widget can be added to the dashboard as it goes (design D3).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockProjectAsync(db, projectId, ct);
        var dashboards = await OrderedAsync(db, projectId, ct);
        var removed = dashboards.Find(d => d.Id == dashboardId);
        if (removed is null)
        {
            return DashboardNotFound();
        }

        if (dashboards.Count == 1)
        {
            return Problems.Conflict(LastOneCode, "A project needs at least one dashboard.");
        }

        // Its widgets go with it: the foreign key cascades.
        if (await db.Dashboards.Where(d => d.Id == dashboardId && d.ProjectId == projectId).ExecuteDeleteAsync(ct) == 0)
        {
            return DashboardNotFound();
        }

        db.Entry(removed).State = EntityState.Detached;
        dashboards.Remove(removed);
        Renumber(dashboards);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Puts the project's dashboards in the order asked for and answers them in it. The order the member's page showed must be the
    /// current one, so a page that missed another member's add, removal or move changes nothing (design D6).
    /// </summary>
    internal static async Task<Results<Ok<List<DashboardEntry>>, ProblemHttpResult>> ReorderAsync(
        Guid projectId, ReorderDashboardsRequest? request, TamizaDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockProjectAsync(db, projectId, ct);
        var dashboards = await OrderedAsync(db, projectId, ct);
        var current = dashboards.Select(d => d.Id).ToList();
        var asked = request?.DashboardIds;
        // The same ids once each: as many as there are, and every one of them.
        if (request?.ExpectedIds is not { } expected || !expected.SequenceEqual(current)
            || asked is null || asked.Count != current.Count || !asked.ToHashSet().SetEquals(current))
        {
            return Problems.Conflict(OrderChangedCode, "The project's dashboards have changed. Reload and try again.");
        }

        var byId = dashboards.ToDictionary(d => d.Id);
        var ordered = asked.Select(id => byId[id]).ToList();
        Renumber(ordered);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(ordered.Select(ToEntry).ToList());
    }

    /// <summary>
    /// Adds a copy of the dashboard right after it, holding its filters and a copy of each of its widgets: same title, chart type,
    /// query and layout, a new id, and the caller as their author. Later changes to one leave the other as it is.
    /// </summary>
    internal static async Task<Results<Created<DashboardEntry>, ValidationProblem, ProblemHttpResult>> DuplicateAsync(
        Guid projectId, Guid dashboardId, DashboardNameRequest? request, TamizaDbContext db, ICurrentUser user,
        IOptions<IndicatorDashboardOptions> options, TimeProvider time, CancellationToken ct)
    {
        if (!await DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardNotFound();
        }

        var (name, error) = DashboardValidation.Name(request?.Name);
        if (error is not null)
        {
            return Problems.Validation("name", error);
        }

        // As for an add, and the source cannot be removed or lose a widget to a move while it is copied (design D3).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockProjectAsync(db, projectId, ct);
        var dashboards = await OrderedAsync(db, projectId, ct);
        var source = dashboards.FindIndex(d => d.Id == dashboardId);
        if (source < 0)
        {
            return DashboardNotFound();
        }

        if (dashboards.Count >= IndicatorDashboardOptions.MaxDashboards)
        {
            return Limit();
        }

        if (await NameTakenAsync(db, projectId, name!, except: Guid.Empty, ct) is { } taken)
        {
            return taken;
        }

        var widgets = await db.DashboardWidgets.AsNoTracking()
            .Where(w => w.DashboardId == dashboardId)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .ToListAsync(ct);
        var max = options.Value.MaxWidgetsPerDashboard;
        // Only possible after an operator lowered the limit: the copy would hold more widgets than a dashboard may.
        if (widgets.Count > max)
        {
            return Problems.Validation("widgets", WidgetLimitMessage(max));
        }

        var now = Now(time);
        var copy = NewDashboard(projectId, name!, source + 1, user, now);
        // The filters as they are now, read under the lock with the source (design D4); the widgets keep what they ignore in their config.
        copy.Filters = dashboards[source].Filters;
        db.Dashboards.Add(copy);
        dashboards.Insert(source + 1, copy);
        Renumber(dashboards);
        db.DashboardWidgets.AddRange(widgets.Select(widget => new DashboardWidget
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            DashboardId = copy.Id,
            ChartType = widget.ChartType,
            Title = widget.Title,
            Config = widget.Config,
            Layout = widget.Layout,
            CreatedBy = user.Id,
            CreatedAt = now,
            UpdatedBy = user.Id,
            UpdatedAt = now,
        }));
        if (await CommitNamedAsync(db, transaction, projectId, name!, ct) is { } raced)
        {
            return raced;
        }

        return TypedResults.Created($"/api/v1/projects/{projectId}/dashboards/{copy.Id}", ToEntry(copy));
    }

    /// <summary>Adds a widget to the dashboard. Without a layout it is placed under that dashboard's widgets.</summary>
    internal static async Task<Results<Created<Widget>, ValidationProblem, ProblemHttpResult>> CreateWidgetAsync(
        Guid projectId, Guid dashboardId, CreateWidgetRequest? request, IndicatorDashboard indicators, TamizaDbContext db, ICurrentUser user,
        IOptions<IndicatorDashboardOptions> options, TimeProvider time, CancellationToken ct)
    {
        if (!await DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardNotFound();
        }

        var errors = new FieldErrors();
        var title = request?.Title?.Trim();
        var chartType = request?.ChartType;
        var config = (request?.Config ?? new WidgetConfigInput()).Normalize();
        IndicatorValidation.CheckTitle(title, errors);
        IndicatorValidation.CheckChartType(chartType, errors);
        if (request?.Layout is not null)
        {
            IndicatorValidation.CheckLayout(request.Layout, errors);
        }

        IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(chartType) ? chartType : null, "config.", errors);
        if (!errors.Any && !await TryCheckAgainstTableAsync(indicators, projectId, config, errors, ct))
        {
            return DataEndpoints.Timeout();
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        // Counting and inserting are one step per project: without the lock, requests that arrive together each count below the
        // limit, and a removal of the dashboard could slip between the check and the insert (design D3).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockProjectAsync(db, projectId, ct);
        if (!await DashboardExistsAsync(db, projectId, dashboardId, ct))
        {
            return DashboardNotFound();
        }

        var existing = await db.DashboardWidgets.AsNoTracking().Where(w => w.DashboardId == dashboardId).Select(w => w.Layout).ToListAsync(ct);
        var max = options.Value.MaxWidgetsPerDashboard;
        if (existing.Count >= max)
        {
            return Problems.Validation("widgets", WidgetLimitMessage(max));
        }

        var layout = request!.Layout ?? PlaceBelow(chartType!, existing.Select(ReadLayout));
        var now = Now(time);
        var widget = new DashboardWidget
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            DashboardId = dashboardId,
            ChartType = chartType!,
            Title = title!,
            Config = JsonSerializer.Serialize(config, Json),
            Layout = JsonSerializer.Serialize(layout, Json),
            CreatedBy = user.Id,
            CreatedAt = now,
            UpdatedBy = user.Id,
            UpdatedAt = now,
        };
        db.DashboardWidgets.Add(widget);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/dashboards/{dashboardId}/widgets/{widget.Id}", ToWidget(widget));
    }

    /// <summary>
    /// Changes any of a widget's title, chart type, query and layout, or moves it to another dashboard of the project. A move or a
    /// rename leaves the query as it is, even if the table no longer has what it names.
    /// </summary>
    internal static async Task<Results<Ok<Widget>, ValidationProblem, ProblemHttpResult>> UpdateWidgetAsync(
        Guid projectId, Guid dashboardId, Guid widgetId, UpdateWidgetRequest? request, IndicatorDashboard indicators, TamizaDbContext db,
        ICurrentUser user, IOptions<IndicatorDashboardOptions> options, TimeProvider time, CancellationToken ct)
    {
        var widget = await db.DashboardWidgets.SingleOrDefaultAsync(
            w => w.Id == widgetId && w.ProjectId == projectId && w.DashboardId == dashboardId, ct);
        if (widget is null)
        {
            return await NotFoundAsync(db, projectId, dashboardId, ct);
        }

        request ??= new UpdateWidgetRequest();
        // Naming the widget's own dashboard moves nothing.
        var target = request.DashboardId is { } asked && asked != widget.DashboardId ? asked : (Guid?)null;
        var errors = new FieldErrors();
        var title = request.Title is null ? widget.Title : request.Title.Trim();
        var chartType = request.ChartType ?? widget.ChartType;
        var config = request.Config is null ? ReadConfig(widget) : request.Config.Normalize();
        var layout = request.Layout ?? ReadLayout(widget.Layout);

        if (request.Title is not null)
        {
            IndicatorValidation.CheckTitle(title, errors);
        }

        IndicatorValidation.CheckChartType(chartType, errors);
        // A moved widget lands below the target's widgets, so a layout sent with the move is ignored (design D9).
        if (request.Layout is not null && target is null)
        {
            IndicatorValidation.CheckLayout(layout, errors);
        }

        if (target is { } to && !await DashboardExistsAsync(db, projectId, to, ct))
        {
            errors.Add("dashboardId", MissingTargetMessage);
        }

        var configChanged = request.Config is not null || request.ChartType is not null;
        if (configChanged)
        {
            IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(chartType) ? chartType : null, "config.", errors);
        }

        if (!errors.Any && request.Config is not null && !await TryCheckAgainstTableAsync(indicators, projectId, config, errors, ct))
        {
            return DataEndpoints.Timeout();
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        // A move changes the number of widgets of the target, so it runs under the lock, where the widget, the target and its count
        // are read again: a widget moved or removed meanwhile is not found, and the target cannot be removed or filled as it lands
        // (design D3, D9). Any other change takes no lock.
        await using var transaction = target is null ? null : await db.Database.BeginTransactionAsync(ct);
        if (target is { } destination)
        {
            await LockProjectAsync(db, projectId, ct);
            await db.Entry(widget).ReloadAsync(ct);
            if (db.Entry(widget).State == EntityState.Detached || widget.DashboardId != dashboardId)
            {
                return await NotFoundAsync(db, projectId, dashboardId, ct);
            }

            if (!await DashboardExistsAsync(db, projectId, destination, ct))
            {
                return Problems.Validation("dashboardId", MissingTargetMessage);
            }

            var existing = await db.DashboardWidgets.AsNoTracking()
                .Where(w => w.DashboardId == destination)
                .Select(w => w.Layout)
                .ToListAsync(ct);
            var max = options.Value.MaxWidgetsPerDashboard;
            if (existing.Count >= max)
            {
                return Problems.Validation("dashboardId", WidgetLimitMessage(max));
            }

            // What the request leaves out keeps the value read under the lock. The chart type and the query go together: a request
            // that sent either saves the pair checked above, as a change that stays on its dashboard does, so a change another member
            // saved meanwhile cannot pair it with what was never checked against it. A request that sent neither saves the pair read
            // under the lock, which was saved together (design D9).
            title = request.Title is null ? widget.Title : title;
            if (!configChanged)
            {
                chartType = widget.ChartType;
                config = ReadConfig(widget);
            }
            // A moved widget keeps the size it has, under the destination's lowest widget.
            var size = ReadLayout(widget.Layout);
            layout = PlaceBelow(existing.Select(ReadLayout), size.W, size.H);
            widget.DashboardId = destination;
        }

        widget.Title = title;
        widget.ChartType = chartType;
        widget.Config = JsonSerializer.Serialize(config, Json);
        widget.Layout = JsonSerializer.Serialize(layout, Json);
        widget.UpdatedBy = user.Id;
        widget.UpdatedAt = Now(time);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The widget was removed after it was read, alone or with its dashboard (design D3).
            return await NotFoundAsync(db, projectId, dashboardId, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Ok(ToWidget(widget));
    }

    internal static async Task<Results<NoContent, ProblemHttpResult>> DeleteWidgetAsync(
        Guid projectId, Guid dashboardId, Guid widgetId, TamizaDbContext db, CancellationToken ct)
    {
        var deleted = await db.DashboardWidgets
            .Where(w => w.Id == widgetId && w.ProjectId == projectId && w.DashboardId == dashboardId)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? await NotFoundAsync(db, projectId, dashboardId, ct) : TypedResults.NoContent();
    }

    /// <summary>
    /// Serializes, until the transaction ends, every change to the project's set or order of dashboards and to the number of widgets
    /// of one of them (design D3).
    /// </summary>
    private static async Task LockProjectAsync(TamizaDbContext db, Guid projectId, CancellationToken ct)
    {
        var lockKey = projectId.ToString();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
    }

    private static Task<bool> DashboardExistsAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, CancellationToken ct) =>
        db.Dashboards.AnyAsync(d => d.Id == dashboardId && d.ProjectId == projectId, ct);

    /// <summary>The dashboard a new project starts with, first and with no widgets, saved together with the project (design D7).</summary>
    internal static Dashboard FirstDashboard(Guid projectId, ICurrentUser user, DateTimeOffset now) =>
        NewDashboard(projectId, FirstName, 0, user, now);

    /// <summary>The project's dashboards in their order, as the project details list them.</summary>
    internal static Task<List<DashboardEntry>> EntriesAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        InOrder(db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId))
            .Select(d => new DashboardEntry(d.Id, d.Name))
            .ToListAsync(ct);

    /// <summary>The project's dashboards in their order, tracked so they can be renumbered.</summary>
    private static Task<List<Dashboard>> OrderedAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        InOrder(db.Dashboards.Where(d => d.ProjectId == projectId)).ToListAsync(ct);

    /// <summary>By position, then creation, then id, so a gap or a tie never hides a dashboard (design D2).</summary>
    private static IOrderedQueryable<Dashboard> InOrder(IQueryable<Dashboard> dashboards) =>
        dashboards.OrderBy(d => d.Position).ThenBy(d => d.CreatedAt).ThenBy(d => d.Id);

    /// <summary>Gives each dashboard its index as position, so a project's positions are always 0 to n - 1 (design D2).</summary>
    private static void Renumber(IReadOnlyList<Dashboard> dashboards)
    {
        for (var i = 0; i < dashboards.Count; i++)
        {
            dashboards[i].Position = i;
        }
    }

    private static Dashboard NewDashboard(Guid projectId, string name, int position, ICurrentUser user, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        ProjectId = projectId,
        Name = name,
        NormalizedName = DashboardValidation.Normalize(name),
        Position = position,
        CreatedBy = user.Id,
        CreatedAt = now,
        UpdatedBy = user.Id,
        UpdatedAt = now,
    };

    private static DashboardEntry ToEntry(Dashboard dashboard) => new(dashboard.Id, dashboard.Name);

    /// <summary>
    /// The 400 on <c>name</c> when a dashboard of the project other than <paramref name="except"/> has the name, whatever its case;
    /// null when none has. The message names that dashboard as it was saved.
    /// </summary>
    private static async Task<ValidationProblem?> NameTakenAsync(
        TamizaDbContext db, Guid projectId, string name, Guid except, CancellationToken ct)
    {
        var normalized = DashboardValidation.Normalize(name);
        var taken = await db.Dashboards.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.NormalizedName == normalized && d.Id != except)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct);
        return taken is null ? null : NameTaken(taken);
    }

    private static ValidationProblem NameTaken(string name) => Problems.Validation("name", DashboardValidation.TakenMessage(name));

    /// <summary>
    /// Saves a new dashboard and commits. A rename takes no lock, so it may give a dashboard of the project the same name after the
    /// check: the unique index refuses the insert, and the answer is the 400 on <c>name</c> the check would have given (design D3).
    /// </summary>
    private static async Task<ValidationProblem?> CommitNamedAsync(
        TamizaDbContext db, IDbContextTransaction transaction, Guid projectId, string name, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsNameTaken(exception))
        {
            await transaction.RollbackAsync(ct);
            return await NameTakenAsync(db, projectId, name, except: Guid.Empty, ct) ?? NameTaken(name);
        }

        await transaction.CommitAsync(ct);
        return null;
    }

    /// <summary>The unique name index refused the write, whether EF wrapped the database's error or not.</summary>
    private static bool IsNameTaken(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException)
            is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NameIndex };

    private static ProblemHttpResult Limit() =>
        Problems.Conflict(LimitCode,
            $"A project can have at most {IndicatorDashboardOptions.MaxDashboards} dashboards. Remove one before adding another.");

    /// <summary>A widget the path does not reach: the dashboard is gone, or it has no such widget.</summary>
    private static async Task<ProblemHttpResult> NotFoundAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, CancellationToken ct) =>
        await DashboardExistsAsync(db, projectId, dashboardId, ct) ? WidgetNotFound() : DashboardNotFound();

    /// <summary>Checks the query against the published table's columns. False when the read timed out.</summary>
    private static async Task<bool> TryCheckAgainstTableAsync(
        IndicatorDashboard indicators, Guid projectId, WidgetConfig config, FieldErrors errors, CancellationToken ct)
    {
        try
        {
            await indicators.CheckConfigAgainstTableAsync(projectId, config, "config.", errors, ct);
            return true;
        }
        catch (DataTimeoutException)
        {
            return false;
        }
    }

    private static string WidgetLimitMessage(int max) =>
        $"A dashboard can have at most {max} widgets. Remove one or move it to another dashboard first.";

    /// <summary>The chart type's default size at the left edge, under the lowest widget.</summary>
    private static WidgetLayout PlaceBelow(string chartType, IEnumerable<WidgetLayout> existing)
    {
        var (w, h) = IndicatorValidation.DefaultSize(chartType);
        return PlaceBelow(existing, w, h);
    }

    /// <summary>A widget of the given size at the left edge, under the lowest widget.</summary>
    private static WidgetLayout PlaceBelow(IEnumerable<WidgetLayout> existing, int w, int h) =>
        new(0, existing.Select(l => l.Y + l.H).DefaultIfEmpty(0).Max(), w, h);

    /// <summary>The time to the microsecond, which is what PostgreSQL keeps, so the response says what was stored.</summary>
    private static DateTimeOffset Now(TimeProvider time)
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static Widget ToWidget(DashboardWidget widget) =>
        new(widget.Id, widget.DashboardId, widget.Title, widget.ChartType, ReadConfig(widget), ReadLayout(widget.Layout), widget.UpdatedAt);

    internal static WidgetConfig ReadConfig(DashboardWidget widget) =>
        (JsonSerializer.Deserialize<WidgetConfigInput>(widget.Config, Json) ?? new WidgetConfigInput()).Normalize();

    private static WidgetLayout ReadLayout(string json) => JsonSerializer.Deserialize<WidgetLayout>(json, Json) ?? new WidgetLayout(0, 0, 1, 1);

    /// <summary>The filters as <see cref="SaveFiltersAsync"/> stored them, which validated them first.</summary>
    internal static List<DashboardFilter> ReadFilters(string json) => JsonSerializer.Deserialize<List<DashboardFilter>>(json, Json) ?? [];

    private static ProblemHttpResult WidgetNotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Widget not found.");

    internal static ProblemHttpResult DashboardNotFound() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Dashboard not found.",
            detail: "The project has no such dashboard. Another member may have removed it.",
            extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
}
