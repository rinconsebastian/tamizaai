using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using Tamiza.Api.Performance;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>The data the pipeline published: which tables exist, what their columns hold and, when rows are allowed, the rows.</summary>
[McpServerToolType]
public sealed class McpDataTools(
    McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db, McpTableReader tables, IndicatorDashboard indicators,
    ColumnProfiler profiler, DataDashboard data, PerformanceDashboard performance)
    : McpToolBase(gate, call, options)
{
    /// <summary>The rows one <c>query_rows</c> call returns at most.</summary>
    public const int MaxRows = 100;

    private const string SourceList = "raw, clean, output, indicators or reference";

    [McpServerTool(Name = "get_tables", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get the tables of a source"),
     Description("The tables of one source with their columns, types, question labels, which select question and choice list each column answers, and row counts. " +
                 "Sources: raw (the flattened submissions, what script 1 receives), clean (script 1's output, what scripts 2 and the indicators receive), output (script 2's output), " +
                 "indicators (the tables the indicator scripts published, what widgets plot) and reference (tables uploaded by analysts). raw and reference need the analyst role. " +
                 "A source with published=false has not been published yet: save script 1 and call start_run.")]
    public Task<CallToolResult> GetTables(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("One of raw, clean, output, indicators, reference.")] string source,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            if (!McpSources.IsKnown(source))
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["source"] = [$"Choose {SourceList}."] });
            }

            await Gate.RequireAsync(projectId, McpSources.RoleToRead(source), McpNeeds.Summaries, ct);
            if (source == McpSources.Reference)
            {
                return await ReferenceAsync(projectId, ct);
            }

            var fields = (await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.FormFields).SingleAsync(ct));
            var form = new McpFormColumns(fields);

            if (source == McpSources.Indicators)
            {
                return await IndicatorsAsync(projectId, ct);
            }

            var published = await tables.ReadAsync(projectId, source, ct);
            return new
            {
                source,
                published.Published,
                published.PublishedAt,
                published.RunId,
                note = published.Published ? null : "No run has published this source yet. Save script 1 and call start_run.",
                tables = published.Tables.Select(t => new
                {
                    t.Name,
                    t.Rows,
                    columns = t.Columns.Select(c => new { c.Name, c.Type, c.Label, answers = source == McpSources.Raw || source == McpSources.Clean ? form.QuestionOf(c.Name) : null }).ToList(),
                }).ToList(),
            };
        }, hint: "Ask for one source at a time.");

    [McpServerTool(Name = "profile_table", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Profile the columns of a table"),
     Description("Describes the columns of one published table over ALL its rows, without returning any row: how many are empty or distinct; for numbers the minimum, quartiles, mean and maximum " +
                 "(a minimum like -99 shows a sentinel value to clean); for dates the earliest and latest; for booleans the counts; for select questions the count of each choice label " +
                 "(a count under 5 is reported as 'fewer than 5'); and for short text columns the values that 5 or more rows share. Free text is never listed. " +
                 "Up to 40 columns per call: name them with columns, or the first 40 are profiled and the others listed. Use it to decide how to clean and which indicators the data supports.")]
    public Task<CallToolResult> ProfileTable(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("One of raw, clean, output, indicators.")] string source,
        [Description("The table's name, from get_tables (for example main).")] string table,
        [Description("Optional. Up to 40 column names to profile.")] string[]? columns = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            if (source is not (McpSources.Raw or McpSources.Clean or McpSources.Output or McpSources.Indicators))
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["source"] = ["Choose raw, clean, output or indicators."] });
            }

            await Gate.RequireAsync(projectId, McpSources.RoleToRead(source), McpNeeds.Summaries, ct);
            var fields = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.FormFields).SingleAsync(ct);
            var form = new McpFormColumns(fields);
            ProfileSelect? SelectOf(string column) => form.QuestionOf(column)?.Kind switch
            {
                "select_one" => ProfileSelect.One,
                "select_multiple" => ProfileSelect.Multiple,
                _ => null,
            };

            TableProfile? profile;
            try
            {
                profile = await profiler.ProfileAsync(
                    projectId, McpSources.SchemaName(projectId, source), McpSources.StepOf(source) ?? IndicatorDashboard.Step, table, columns, SelectOf,
                    Settings.ProfileStatementTimeout, ct);
            }
            catch (ProfileColumnsNotFoundException missing)
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["columns"] = [$"The table has no column {string.Join(", ", missing.Names)}. Call get_tables to see its columns."] });
            }

            return profile ?? throw McpRefusal.NotFound($"The table '{table}' in {source}", "Call get_tables to see which tables are published.");
        }, hint: "Name fewer columns with the columns argument.");

    [McpServerTool(Name = "get_fieldwork", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get the fieldwork metrics"),
     Description("The performance dashboard for the project: submissions per day, completion time, the work of each enumerator and the coverage of the sampling frame, computed on the stored submissions. " +
                 "Optionally for a range of days (YYYY-MM-DD, in the project's time zone). It needs no published tables, so it works before any script exists.")]
    public Task<CallToolResult> GetFieldwork(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("Optional. First day of the range, YYYY-MM-DD.")] string? from = null,
        [Description("Optional. Last day of the range, YYYY-MM-DD.")] string? to = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<PerformanceDashboardResult>(await PerformanceEndpoints.GetAsync(projectId, from, to, db, performance, ct));
        }, hint: "Ask for a shorter range with from and to.");

    [McpServerTool(Name = "query_rows", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Read rows of a table"),
     Description("Rows of one table, up to 100 per call, with the clean data dashboard's filters and one sort column. ONLY available when both the operator and the project's admin allowed rows " +
                 "(otherwise refused with mcp.rowsNotAllowed; use profile_table instead). Filters are objects { column, op, value | values | min | max } combined with AND; ops are contains and equals (text), in (a list of values), range (numbers, dates and timestamps, with min and/or max, both included), empty and notEmpty. " +
                 "Select answers are choice labels. Text over 500 characters is cut. Reference tables can be paged but not filtered.")]
    public Task<CallToolResult> QueryRows(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("One of raw, clean, output, indicators, reference.")] string source,
        [Description("The table's name, from get_tables.")] string table,
        [Description("Optional. Names of the columns to return; all when omitted.")] string[]? columns = null,
        [Description("Optional. Filters, at most one per column.")] DataFilter[]? filters = null,
        [Description("Optional. The column to sort by.")] string? sortBy = null,
        [Description("Optional. asc or desc; asc by default.")] string? sortDirection = null,
        [Description("Optional. The page, from 1.")] int? page = null,
        [Description("Optional. Rows per page: 25, 50 or 100. 50 by default.")] int? pageSize = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            if (!McpSources.IsKnown(source))
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["source"] = [$"Choose {SourceList}."] });
            }

            await Gate.RequireAsync(projectId, McpSources.RoleToRead(source), McpNeeds.Rows, ct);
            return source == McpSources.Reference
                ? await ReferenceRowsAsync(projectId, table, columns, filters, sortBy, page, pageSize, ct)
                : await PublishedRowsAsync(projectId, source, table, columns, filters, sortBy, sortDirection, page, pageSize, ct);
        }, hint: "Ask for a smaller page with pageSize, or name fewer columns.");

    private async Task<object> PublishedRowsAsync(
        Guid projectId, string source, string table, string[]? columns, DataFilter[]? filters, string? sortBy, string? sortDirection, int? page, int? pageSize,
        CancellationToken ct)
    {
        var request = new DataQueryRequest(filters, sortBy is null ? null : new DataSort(sortBy, sortDirection ?? "asc"), page, pageSize ?? 50);
        var step = McpSources.StepOf(source) ?? IndicatorDashboard.Step;
        var result = await data.QuerySchemaAsync(projectId, McpSources.SchemaName(projectId, source), step, table, request, ct)
            ?? throw McpRefusal.NotFound($"The table '{table}' in {source}", "Call get_tables to see which tables are published.");
        if (result.Errors is { } errors)
        {
            throw McpRefusal.Validation(errors);
        }

        var pageData = result.Page!;
        var keep = Enumerable.Range(0, pageData.Columns.Count).ToList();
        if (columns is { Length: > 0 })
        {
            var names = pageData.Columns.Select(c => c.Name).ToList();
            var missing = columns.Where(c => !names.Contains(c, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["columns"] = [$"The table has no column {string.Join(", ", missing)}."] });
            }

            keep = columns.Distinct(StringComparer.Ordinal).Select(c => names.IndexOf(c)).ToList();
        }

        var cut = pageData.CutCells.Where(c => keep.Contains(c[1])).Select(c => new[] { c[0], keep.IndexOf(c[1]) }).ToList();
        return new
        {
            source,
            table,
            columns = keep.Select(i => new { pageData.Columns[i].Name, pageData.Columns[i].Type }).ToList(),
            rows = pageData.Rows.Select(row => keep.Select(i => row[i]).ToList()).ToList(),
            total = pageData.Total,
            pageData.Page,
            pageData.PageSize,
            cutCells = cut,
            pageData.PublishedAt,
            pageData.TimeZone,
        };
    }

    private async Task<object> ReferenceRowsAsync(
        Guid projectId, string table, string[]? columns, DataFilter[]? filters, string? sortBy, int? page, int? pageSize, CancellationToken ct)
    {
        if (filters is { Length: > 0 } || sortBy is not null)
        {
            throw McpRefusal.Validation(new Dictionary<string, string[]> { ["filters"] = ["Reference tables can be paged but not filtered or sorted."] });
        }

        var size = pageSize ?? 50;
        if (size is not (25 or 50 or 100))
        {
            throw McpRefusal.Validation(new Dictionary<string, string[]> { ["pageSize"] = ["Choose 25, 50 or 100 rows per page."] });
        }

        var current = Math.Max(1, page ?? 1);
        var reference = await db.ReferenceTables.AsNoTracking().SingleOrDefaultAsync(t => t.ProjectId == projectId && t.Name == table, ct)
            ?? throw McpRefusal.NotFound($"The reference table '{table}'", "Call get_tables with source reference to see them.");
        var content = await db.ReferenceTableContents.AsNoTracking().SingleAsync(c => c.ReferenceTableId == reference.Id, ct);
        var all = JsonSerializer.Deserialize<List<List<JsonElement>>>(content.Rows) ?? [];
        var names = reference.Columns.Select(c => c.Name).ToList();
        var keep = Enumerable.Range(0, names.Count).ToList();
        if (columns is { Length: > 0 })
        {
            var missing = columns.Where(c => !names.Contains(c, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["columns"] = [$"The table has no column {string.Join(", ", missing)}."] });
            }

            keep = columns.Distinct(StringComparer.Ordinal).Select(c => names.IndexOf(c)).ToList();
        }

        return new
        {
            source = McpSources.Reference,
            table,
            columns = keep.Select(i => new { reference.Columns[i].Name, reference.Columns[i].Type }).ToList(),
            rows = all.Skip((current - 1) * size).Take(size).Select(row => keep.Select(i => i < row.Count ? row[i] : default).ToList()).ToList(),
            total = (long)all.Count,
            page = current,
            pageSize = size,
        };
    }

    private async Task<object> ReferenceAsync(Guid projectId, CancellationToken ct)
    {
        var references = await db.ReferenceTables.AsNoTracking().Where(t => t.ProjectId == projectId).OrderBy(t => t.Name).ToListAsync(ct);
        return new
        {
            source = McpSources.Reference,
            published = references.Count > 0,
            tables = references.Select(t => new { t.Name, t.FileName, rows = t.RowCount, columns = t.Columns.Select(c => new { c.Name, c.Type }).ToList() }).ToList(),
        };
    }

    private async Task<object> IndicatorsAsync(Guid projectId, CancellationToken ct)
    {
        var publications = await indicators.GetPublicationsAsync(projectId, ct);
        var result = new List<object>();
        foreach (var (name, publication) in publications.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var columns = await indicators.GetColumnsAsync(projectId, name, ct);
            result.Add(new
            {
                name,
                rows = publication.Rows,
                publication.PublishedAt,
                columns = columns.Columns,
                measures = columns.Numeric,
                defaults = columns.Defaults,
            });
        }

        return new { source = McpSources.Indicators, published = result.Count > 0, tables = result };
    }
}
