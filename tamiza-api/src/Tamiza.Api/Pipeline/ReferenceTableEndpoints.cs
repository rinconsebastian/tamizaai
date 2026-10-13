using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

/// <summary>Tables analysts upload for the project's scripts, read through <c>ctx.reference</c>.</summary>
public static class ReferenceTableEndpoints
{
    public const int MaxTables = 50;
    public const int PreviewRows = 100;
    public const string ExistsCode = "reference.exists";
    public const string LimitCode = "reference.limit";

    public static RouteGroupBuilder MapReferenceTableEndpoints(this RouteGroupBuilder api)
    {
        var tables = api.MapGroup("/projects/{projectId:guid}/reference-tables").WithTags("Pipeline");
        tables.MapGet("", ListAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ListReferenceTables");
        tables.MapGet("/{name}", GetAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetReferenceTable");
        tables.MapPost("", UploadAsync)
            .RequireProjectRole(ProjectRole.Analyst)
            .DisableAntiforgery() // bearer-token API: no cookies to forge
            .WithMetadata(new RequestSizeLimitAttribute(2 * ReferenceTableImport.MaxFileBytes))
            .WithName("UploadReferenceTable");
        tables.MapPut("/{name}", ReplaceAsync)
            .RequireProjectRole(ProjectRole.Analyst)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(2 * ReferenceTableImport.MaxFileBytes))
            .WithName("ReplaceReferenceTable");
        tables.MapDelete("/{name}", DeleteAsync).RequireProjectRole(ProjectRole.Analyst).WithName("DeleteReferenceTable");
        return api;
    }

    private static readonly Expression<Func<ReferenceTable, ReferenceTableSummary>> ToSummary = t => new ReferenceTableSummary(
        t.Name, t.FileName, t.RowCount, t.Columns, new Person(t.UpdatedByUser!.Id, t.UpdatedByUser.Name), t.CreatedAt, t.UpdatedAt);

    private static async Task<Ok<List<ReferenceTableSummary>>> ListAsync(Guid projectId, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.ReferenceTables.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Name)
            .Select(ToSummary)
            .ToListAsync(ct));

    private static async Task<Results<Ok<ReferenceTablePreview>, NotFound>> GetAsync(Guid projectId, string name, TamizaDbContext db, CancellationToken ct)
    {
        var table = await FindAsync(db, projectId, name, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        var rows = await db.Database
            .SqlQuery<string>($"""
                SELECT jsonb_path_query_array(rows, '$[0 to 99]')::text AS "Value"
                FROM tamiza.reference_table_contents WHERE reference_table_id = {table.Value.Id}
                """)
            .SingleAsync(ct);
        return TypedResults.Ok(new ReferenceTablePreview(table.Value.Summary, JsonSerializer.Deserialize<List<List<JsonElement>>>(rows)!));
    }

    private static async Task<Results<Created<ReferenceTableUpload>, ValidationProblem, ProblemHttpResult>> UploadAsync(
        Guid projectId, IFormFile? file, [FromForm] string? name, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (!ColumnNames.IsValidTableName(name))
        {
            return Problems.Validation("name",
                "Use lowercase letters, digits and underscores, starting with a letter, at most 63 characters (for example population_2026).");
        }

        var (import, fileError) = await ImportAsync(file, ct);
        if (fileError is not null)
        {
            return fileError;
        }

        if (await db.ReferenceTables.AnyAsync(t => t.ProjectId == projectId && t.Name == name, ct))
        {
            return Exists(name!);
        }

        if (await db.ReferenceTables.CountAsync(t => t.ProjectId == projectId, ct) >= MaxTables)
        {
            return Problems.Conflict(LimitCode, $"The project already has {MaxTables} reference tables. Delete one before adding another.");
        }

        var now = time.GetUtcNow();
        var table = new ReferenceTable
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            Name = name!,
            FileName = FileName(file!),
            Columns = import!.Columns,
            RowCount = import.RowCount,
            CreatedAt = now,
            UpdatedBy = user.Id,
            UpdatedAt = now,
        };
        db.ReferenceTables.Add(table);
        db.ReferenceTableContents.Add(new ReferenceTableContent { ReferenceTableId = table.Id, Rows = import.RowsJson });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Exists(name!);
        }

        var summary = await db.ReferenceTables.AsNoTracking().Where(t => t.Id == table.Id).Select(ToSummary).SingleAsync(ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/reference-tables/{table.Name}", new ReferenceTableUpload(summary, import.Renamed));
    }

    private static async Task<Results<Ok<ReferenceTableUpload>, ValidationProblem, NotFound>> ReplaceAsync(
        Guid projectId, string name, IFormFile? file, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        if (await FindAsync(db, projectId, name, ct) is null)
        {
            return TypedResults.NotFound();
        }

        var (import, fileError) = await ImportAsync(file, ct);
        if (fileError is not null)
        {
            return fileError;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var table = await db.ReferenceTables.SingleOrDefaultAsync(t => t.ProjectId == projectId && t.Name == name, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.FileName = FileName(file!);
        table.Columns = import!.Columns;
        table.RowCount = import.RowCount;
        table.UpdatedBy = user.Id;
        table.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.ReferenceTableContents.Where(c => c.ReferenceTableId == table.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.Rows, import.RowsJson), ct);
        await transaction.CommitAsync(ct);

        var summary = await db.ReferenceTables.AsNoTracking().Where(t => t.Id == table.Id).Select(ToSummary).SingleAsync(ct);
        return TypedResults.Ok(new ReferenceTableUpload(summary, import.Renamed));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid projectId, string name, TamizaDbContext db, CancellationToken ct)
    {
        if (!ColumnNames.IsValidTableName(name))
        {
            return TypedResults.NotFound();
        }

        var deleted = await db.ReferenceTables.Where(t => t.ProjectId == projectId && t.Name == name).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<(Guid Id, ReferenceTableSummary Summary)?> FindAsync(TamizaDbContext db, Guid projectId, string name, CancellationToken ct)
    {
        if (!ColumnNames.IsValidTableName(name))
        {
            return null;
        }

        var found = await db.ReferenceTables.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.Name == name)
            .Select(t => new { t.Id })
            .SingleOrDefaultAsync(ct);
        if (found is null)
        {
            return null;
        }

        return (found.Id, await db.ReferenceTables.AsNoTracking().Where(t => t.Id == found.Id).Select(ToSummary).SingleAsync(ct));
    }

    /// <summary>Checks and parses the uploaded file; the error is a validation problem on <c>file</c>.</summary>
    private static async Task<(ReferenceImport? Import, ValidationProblem? Error)> ImportAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return (null, Problems.Validation("file", "Choose a CSV or XLSX file."));
        }

        if (file.Length > ReferenceTableImport.MaxFileBytes)
        {
            return (null, Problems.Validation("file", "The file is larger than 10 MB."));
        }

        if (!SpreadsheetReader.IsSupported(file.FileName, out var type))
        {
            return (null, Problems.Validation("file", "Only CSV and XLSX files are accepted."));
        }

        // XLSX needs a seekable stream, and the file is read twice; it is at most 10 MB.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var (import, error) = ReferenceTableImport.Read(buffer, type);
        return error is null ? (import, null) : (null, Problems.Validation("file", error));
    }

    private static ProblemHttpResult Exists(string name) =>
        Problems.Conflict(ExistsCode, $"The project already has a reference table named {name}. Replace it or choose another name.");

    private static string FileName(IFormFile file)
    {
        var name = Path.GetFileName(file.FileName);
        return name.Length <= 255 ? name : name[^255..];
    }
}
