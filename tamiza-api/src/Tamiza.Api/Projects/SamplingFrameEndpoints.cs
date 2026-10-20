using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;

namespace Tamiza.Api.Projects;

public sealed record SamplingFrameView(IReadOnlyList<SamplingDimension> Dimensions, IReadOnlyList<SamplingTarget> Targets, DateTimeOffset UpdatedAt);

/// <summary>A prepared workbook: its single-use link, its name and how many rows its Frame sheet holds.</summary>
public sealed record SamplingFrameFile(string DownloadUrl, string FileName, long RowCount, DateTimeOffset ExpiresAt);

public static class SamplingFrameEndpoints
{
    public const string NoFrameCode = "samplingFrame.none";

    public static RouteGroupBuilder MapSamplingFrameEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}/sampling-frame").WithTags("Sampling frame");
        project.MapGet("", GetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetSamplingFrame");
        project.MapPut("", SaveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveSamplingFrame");
        project.MapPost("/import", ImportAsync)
            .RequireProjectRole(ProjectRole.Analyst)
            .DisableAntiforgery() // bearer-token API: no cookies to forge
            .WithMetadata(new RequestSizeLimitAttribute(2 * SamplingFrameImport.MaxFileBytes))
            .WithName("PreviewSamplingFrameImport");
        project.MapPost("/template", DownloadTemplateAsync).RequireProjectRole(ProjectRole.Analyst).WithName("DownloadSamplingFrameTemplate");
        project.MapPost("/export", DownloadAsync).RequireProjectRole(ProjectRole.Analyst).WithName("DownloadSamplingFrame");
        return api;
    }

    private static async Task<Results<Ok<SamplingFrameView>, NoContent>> GetAsync(Guid projectId, TamizaDbContext db, CancellationToken ct)
    {
        var frame = await db.SamplingFrames.AsNoTracking().SingleOrDefaultAsync(f => f.ProjectId == projectId, ct);
        return frame is null ? TypedResults.NoContent() : TypedResults.Ok(new SamplingFrameView(frame.Dimensions, frame.Targets, frame.UpdatedAt));
    }

    private static async Task<Results<Ok<SamplingFrameView>, ValidationProblem>> SaveAsync(
        Guid projectId, SamplingFrameInput input, TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var formFields = (await db.Projects.Where(p => p.Id == projectId).Select(p => p.FormFields).SingleAsync(ct))
            .Select(f => f.Xpath).ToHashSet(StringComparer.Ordinal);
        var errors = new FieldErrors();
        var (dimensions, targets) = SamplingFrameValidator.Validate(input, formFields, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var frame = await db.SamplingFrames.SingleOrDefaultAsync(f => f.ProjectId == projectId, ct);
        if (frame is null)
        {
            frame = new SamplingFrame { ProjectId = projectId };
            db.SamplingFrames.Add(frame);
        }

        frame.Dimensions = dimensions;
        frame.Targets = targets;
        frame.UpdatedBy = user.Id;
        frame.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new SamplingFrameView(frame.Dimensions, frame.Targets, frame.UpdatedAt));
    }

    private static async Task<Results<Ok<ImportPreview>, ValidationProblem>> ImportAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problems.Validation("file", "Choose a CSV or XLSX file.");
        }

        if (file.Length > SamplingFrameImport.MaxFileBytes)
        {
            return Problems.Validation("file", "The file is larger than 5 MB.");
        }

        if (!SpreadsheetReader.IsSupported(file.FileName, out var type))
        {
            return Problems.Validation("file", "Use a .csv or .xlsx file.");
        }

        // XLSX needs a seekable stream; the file is at most 5 MB.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var (preview, fileError) = SamplingFrameImport.Read(buffer, type);
        return fileError is not null ? Problems.Validation("file", fileError) : TypedResults.Ok(preview!);
    }

    /// <summary>
    /// Writes an import template for the chosen questions and registers a single-use link to it (design D2, D3, D7). The file is
    /// small and quick to write, so it does not wait for an export slot.
    /// </summary>
    private static async Task<Results<Ok<SamplingFrameFile>, ValidationProblem>> DownloadTemplateAsync(
        Guid projectId, SamplingFrameTemplateRequest? request, TamizaDbContext db, ICurrentUser user, ExportRegistry exports, TimeProvider time,
        CancellationToken ct)
    {
        var project = await FormAsync(db, projectId, ct);
        var errors = new FieldErrors();
        var plan = SamplingFrameTemplate.Plan(request ?? new SamplingFrameTemplateRequest(null), project.FormFields, project.FormChoices, errors);
        if (plan is null)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var link = Register(exports, projectId, user.Id, ExportFileName.Create(project.Name, "sampling-frame-template", day, "xlsx"), plan.RowCount,
            path => SamplingFrameWorkbook.WriteTemplate(path, plan, new FrameFileSource(project.Name, day, project.FormVersion)).Written);
        return link is null
            ? Problems.Validation("dimensions", "This template would be larger than the 5 MB import limit. Choose fewer choices or questions.")
            : TypedResults.Ok(link);
    }

    /// <summary>Writes the saved frame in the template's layout, with its targets, and registers a single-use link to it.</summary>
    private static async Task<Results<Ok<SamplingFrameFile>, ProblemHttpResult>> DownloadAsync(
        Guid projectId, TamizaDbContext db, ICurrentUser user, ExportRegistry exports, TimeProvider time, CancellationToken ct)
    {
        var frame = await db.SamplingFrames.AsNoTracking().SingleOrDefaultAsync(f => f.ProjectId == projectId, ct);
        if (frame is null)
        {
            return Problems.Conflict(NoFrameCode, "This project has no sampling frame yet.");
        }

        var project = await FormAsync(db, projectId, ct);
        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var link = Register(exports, projectId, user.Id, ExportFileName.Create(project.Name, "sampling-frame", day, "xlsx"), frame.Targets.Count,
            path => SamplingFrameWorkbook.WriteFrame(path, frame.Dimensions, frame.Targets, project.FormFields, project.FormChoices,
                new FrameFileSource(project.Name, day, project.FormVersion)).Written);
        return TypedResults.Ok(link!);
    }

    private sealed record ProjectForm(string Name, List<FormField> FormFields, List<FormChoice> FormChoices, string? FormVersion);

    private static Task<ProjectForm> FormAsync(TamizaDbContext db, Guid projectId, CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new ProjectForm(p.Name, p.FormFields, p.FormChoices, p.FormVersion))
            .SingleAsync(ct);

    /// <summary>
    /// Writes a file in the export directory with <paramref name="write"/> and registers it. Returns null, leaving no file, when
    /// <paramref name="write"/> wrote nothing; the file is deleted too when anything fails before the registry owns it.
    /// </summary>
    private static SamplingFrameFile? Register(ExportRegistry exports, Guid projectId, Guid userId, string fileName, long rowCount, Func<string, bool> write)
    {
        string? path = exports.NewFilePath();
        try
        {
            if (!write(path))
            {
                return null;
            }

            var link = exports.Add(projectId, userId, path, fileName, DataDashboard.XlsxContentType);
            path = null; // the registry owns the file now
            return new SamplingFrameFile($"/api/v1/data-exports/{link.Token}", fileName, rowCount, link.ExpiresAt);
        }
        finally
        {
            if (path is not null)
            {
                ExportRegistry.DeleteQuietly(path);
            }
        }
    }
}
