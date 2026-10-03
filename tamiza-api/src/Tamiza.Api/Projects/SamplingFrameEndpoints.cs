using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;

namespace Tamiza.Api.Projects;

public sealed record SamplingFrameView(IReadOnlyList<SamplingDimension> Dimensions, IReadOnlyList<SamplingTarget> Targets, DateTimeOffset UpdatedAt);

public static class SamplingFrameEndpoints
{
    public static RouteGroupBuilder MapSamplingFrameEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}/sampling-frame").WithTags("Sampling frame");
        project.MapGet("", GetAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetSamplingFrame");
        project.MapPut("", SaveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveSamplingFrame");
        project.MapPost("/import", ImportAsync)
            .RequireProjectRole(ProjectRole.Analyst)
            .DisableAntiforgery() // bearer-token API: no cookies to forge
            .WithMetadata(new RequestSizeLimitAttribute(2 * SamplingFrameImport.MaxFileBytes))
            .WithName("PreviewSamplingFrameImport");
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

        if (!SamplingFrameImport.IsSupported(file.FileName, out var type))
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
}
