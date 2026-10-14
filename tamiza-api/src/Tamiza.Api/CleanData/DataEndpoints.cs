using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.CleanData;

public static class DataEndpoints
{
    public const string TableNotFoundCode = "data.tableNotFound";
    public const string ColumnNotFoundCode = "data.columnNotFound";

    public static RouteGroupBuilder MapDataEndpoints(this RouteGroupBuilder api)
    {
        var data = api.MapGroup("/projects/{projectId:guid}/data").WithTags("Clean data");
        data.MapGet("", GetOverviewAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetDataOverview");
        data.MapPost("/{source}/{table}/query", QueryAsync).RequireProjectRole(ProjectRole.Viewer).WithName("QueryDataTable");
        data.MapGet("/{source}/{table}/columns/{column}/values", GetValuesAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetDataColumnValues");
        data.MapPost("/{source}/{table}/exports", ExportAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ExportDataTable");
        // The one public endpoint: the link is the credential (single use, short lived, one file). See the auth spec.
        api.MapGroup("/data-exports").WithTags("Clean data").MapGet("/{token}", Download).AllowAnonymous().WithName("DownloadDataExport");
        return api;
    }

    /// <summary>The published sources with their tables, the source to open on and the project's time zone.</summary>
    private static async Task<Results<Ok<DataOverview>, ProblemHttpResult>> GetOverviewAsync(
        Guid projectId, DataDashboard dashboard, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await dashboard.GetOverviewAsync(projectId, ct));
        }
        catch (DataTimeoutException)
        {
            return Timeout();
        }
    }

    /// <summary>One page of a published table, with the filters and sort of the body applied on the server.</summary>
    private static async Task<Results<Ok<DataPage>, ValidationProblem, ProblemHttpResult>> QueryAsync(
        Guid projectId, string source, string table, DataQueryRequest? request, DataDashboard dashboard, CancellationToken ct)
    {
        try
        {
            var result = await dashboard.QueryAsync(projectId, source, table, request ?? new DataQueryRequest(), ct);
            return result switch
            {
                null => TableNotFound(),
                { Errors: { } errors } => Problems.Validation(errors),
                _ => TypedResults.Ok(result.Page!),
            };
        }
        catch (DataTimeoutException)
        {
            return Timeout();
        }
    }

    /// <summary>The most frequent values of a text or true/false column, optionally only those that contain <c>search</c>.</summary>
    private static async Task<Results<Ok<DataValueList>, ValidationProblem, ProblemHttpResult>> GetValuesAsync(
        Guid projectId, string source, string table, string column, string? search, DataDashboard dashboard, CancellationToken ct)
    {
        if (search is { Length: > DataQueryBuilder.MaxTextLength })
        {
            return Problems.Validation("search", $"The search text is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }

        try
        {
            var result = await dashboard.ValuesAsync(projectId, source, table, column, search, ct);
            return result switch
            {
                null => TableNotFound(),
                { Problem: DataValuesProblem.ColumnNotFound } => ColumnNotFound(column),
                { Problem: DataValuesProblem.Unsupported } => Problems.Validation("column", $"Column '{column}' is not a text or true/false column, so it has no list of values."),
                _ => TypedResults.Ok(new DataValueList(result.Values!)),
            };
        }
        catch (DataTimeoutException)
        {
            return Timeout();
        }
    }

    /// <summary>Prepares a file with the whole table, filters and sort applied, and returns the link that downloads it once.</summary>
    private static async Task<Results<Ok<DataExportResult>, ValidationProblem, ProblemHttpResult>> ExportAsync(
        Guid projectId, string source, string table, DataExportRequest? request, DataDashboard dashboard, ICurrentUser user, CancellationToken ct)
    {
        if (request?.Format is not (DataDashboard.Csv or DataDashboard.Xlsx))
        {
            return Problems.Validation("format", "The format must be 'csv' or 'xlsx'.");
        }

        try
        {
            var result = await dashboard.ExportAsync(projectId, user.Id, source, table, request, ct);
            return result switch
            {
                null => TableNotFound(),
                { Errors: { } errors } => Problems.Validation(errors),
                _ => TypedResults.Ok(result.Export!),
            };
        }
        catch (DataTimeoutException)
        {
            return Timeout();
        }
        catch (DataExportBusyException)
        {
            return Problems.Unavailable(DataExportBusyException.Code, "Other exports are being prepared. Try again in a moment.");
        }
        catch (DataXlsxTooManyRowsException ex)
        {
            return Unprocessable(DataXlsxTooManyRowsException.Code,
                string.Create(CultureInfo.InvariantCulture, $"The table has {ex.Rows:N0} matching rows, more than the {ex.Limit:N0} that fit an XLSX sheet. Export to CSV or add a filter."));
        }
        catch (ExportTooLargeException ex)
        {
            return Unprocessable(DataDashboard.ExportTooLargeCode,
                string.Create(CultureInfo.InvariantCulture, $"The export is larger than the limit of {ex.Limit:N0} bytes. Add a filter to export fewer rows."));
        }
    }

    /// <summary>Streams a prepared file once. The link was issued to a signed-in member; it carries no token, so the secret in it is the credential.</summary>
    private static IResult Download(string token, ExportRegistry registry, HttpContext http)
    {
        var export = registry.TryTake(token);
        if (export is null)
        {
            return TypedResults.NotFound();
        }

        FileStream stream;
        try
        {
            // The file is deleted when the response ends, finished or aborted.
            stream = new FileStream(export.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.File(stream, export.ContentType, export.FileName);
    }

    internal static ProblemHttpResult Unprocessable(string code, string detail) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "The export could not be prepared.",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    internal static ProblemHttpResult Timeout() =>
        Problems.Unavailable(DataTimeoutException.Code, "Reading the table took too long. Try a narrower filter or try again in a moment.");

    internal static ProblemHttpResult ColumnNotFound(string column) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The column is not in the table.",
            detail: $"The table has no column '{column}'. A run may have replaced the table.",
            extensions: new Dictionary<string, object?> { ["code"] = ColumnNotFoundCode });

    internal static ProblemHttpResult TableNotFound() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The table is not published.",
            detail: "The source or table is not published. A run may have replaced the tables.",
            extensions: new Dictionary<string, object?> { ["code"] = TableNotFoundCode });
}
