using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

public static partial class ProjectEndpoints
{
    private const int MaxNameLength = 200;

    [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
    private static partial Regex AssetUidPattern();

    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var projects = api.MapGroup("/projects").WithTags("Projects");
        projects.MapGet("", ListAsync).WithName("ListProjects");
        projects.MapPost("", CreateAsync).WithName("CreateProject");

        var project = projects.MapGroup("/{projectId:guid}");
        project.MapGet("", GetAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetProject");
        project.MapPatch("", UpdateAsync).RequireProjectRole(ProjectRole.Admin).WithName("UpdateProject");
        project.MapPut("/kobo-token", ReplaceTokenAsync).RequireProjectRole(ProjectRole.Admin).WithName("ReplaceKoboToken");
        project.MapPost("/kobo-check", RecheckAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RecheckKoboConnection");
        project.MapGet("/form-fields", GetFormFieldsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetFormFields");
        project.MapGet("/webhook", GetWebhookAsync).RequireProjectRole(ProjectRole.Admin).WithName("GetWebhookSettings");
        project.MapPost("/webhook/secret", RegenerateSecretAsync).RequireProjectRole(ProjectRole.Admin).WithName("RegenerateWebhookSecret");
        return api;
    }

    private static async Task<Ok<List<ProjectSummary>>> ListAsync(TamizaDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var memberships = db.ProjectMembers.Where(m => m.UserId == user.Id);
        var rows = user.IsSuperAdmin
            ? await db.Projects
                .Select(p => new { p.Id, p.Name, p.FormName, Role = memberships.Where(m => m.ProjectId == p.Id).Select(m => (ProjectRole?)m.Role).FirstOrDefault() })
                .ToListAsync(ct)
            : await db.Projects
                .Join(memberships, p => p.Id, m => m.ProjectId, (p, m) => new { p.Id, p.Name, p.FormName, Role = (ProjectRole?)m.Role })
                .ToListAsync(ct);

        // Superadmins act as admin on projects they are not members of.
        return TypedResults.Ok(rows
            .Select(r => new ProjectSummary(r.Id, r.Name, r.FormName, r.Role ?? ProjectRole.Admin))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<Results<Created<CreatedProject>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateProjectRequest request, TamizaDbContext db, ICurrentUser user, KoboClient kobo, SecretProtector secrets,
        IOptions<TamizaOptions> options, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var name = ValidateName(request.Name, errors);
        var assetUid = request.AssetUid?.Trim() ?? "";
        if (!AssetUidPattern().IsMatch(assetUid))
        {
            errors.Add("assetUid", "Enter the form's asset UID: letters and digits only, as shown in the form's URL in Kobo.");
        }

        var token = ValidateToken(request.ApiToken, errors);
        var (serverUrl, urlError) = KoboServerUrl.Parse(request.KoboServerUrl, options.Value.KoboAllowPrivateNetworks);
        if (urlError?.Code == KoboErrorCodes.InvalidUrl)
        {
            errors.Add("koboServerUrl", urlError.Message);
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        if (urlError is not null)
        {
            return Problems.Kobo(urlError);
        }

        var (asset, koboError) = await kobo.VerifyAccessAsync(serverUrl!, assetUid, token, ct);
        if (koboError is not null)
        {
            return Problems.Kobo(koboError);
        }

        var now = time.GetUtcNow();
        var webhookSecret = WebhookSecret.Generate();
        var project = new Project
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            KoboServerUrl = serverUrl!,
            KoboAssetUid = assetUid,
            EncryptedApiToken = secrets.Protect(token),
            EncryptedWebhookSecret = secrets.Protect(webhookSecret),
            FormName = asset!.Name,
            FormFields = asset.Fields.ToList(),
            FormCheckedAt = now,
            CreatedBy = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = user.Id, Role = ProjectRole.Admin, CreatedAt = now });
        await db.SaveChangesAsync(ct);

        var details = ToDetails(project, ProjectRole.Admin, tokenUnreadable: false);
        var webhook = new WebhookSettings(WebhookUrl(options.Value, project.Id), WebhookSecret.Username, webhookSecret, false);
        return TypedResults.Created($"/api/v1/projects/{project.Id}", new CreatedProject(details, webhook));
    }

    private static async Task<Ok<ProjectDetails>> GetAsync(Guid projectId, HttpContext http, TamizaDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        return TypedResults.Ok(ToDetails(project, http.GetProjectRole(), !secrets.TryUnprotect(project.EncryptedApiToken, out _)));
    }

    private static async Task<Results<Ok<ProjectDetails>, ValidationProblem>> UpdateAsync(
        Guid projectId, UpdateProjectRequest request, HttpContext http, TamizaDbContext db, SecretProtector secrets, TimeProvider time, CancellationToken ct)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var errors = new FieldErrors();
        if (request.Name is not null)
        {
            project.Name = ValidateName(request.Name, errors);
        }

        if (request.EnumeratorField is not null)
        {
            var field = request.EnumeratorField.Trim();
            if (field.Length == 0)
            {
                project.EnumeratorField = null;
            }
            else if (project.FormFields.Any(f => f.Xpath == field))
            {
                project.EnumeratorField = field;
            }
            else
            {
                errors.Add("enumeratorField", $"The form has no field '{field}'.");
            }
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        project.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDetails(project, http.GetProjectRole(), !secrets.TryUnprotect(project.EncryptedApiToken, out _)));
    }

    private static async Task<Results<Ok<ProjectDetails>, ValidationProblem, ProblemHttpResult>> ReplaceTokenAsync(
        Guid projectId, ReplaceTokenRequest request, HttpContext http, TamizaDbContext db, KoboClient kobo, SecretProtector secrets,
        TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var token = ValidateToken(request.ApiToken, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var (asset, koboError) = await kobo.VerifyAccessAsync(project.KoboServerUrl, project.KoboAssetUid, token, ct);
        if (koboError is not null)
        {
            return Problems.Kobo(koboError);
        }

        project.EncryptedApiToken = secrets.Protect(token);
        ApplySnapshot(project, asset!, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDetails(project, http.GetProjectRole(), tokenUnreadable: false));
    }

    private static async Task<Results<Ok<ProjectDetails>, ProblemHttpResult>> RecheckAsync(
        Guid projectId, HttpContext http, TamizaDbContext db, KoboClient kobo, SecretProtector secrets, TimeProvider time, CancellationToken ct)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        if (!secrets.TryUnprotect(project.EncryptedApiToken, out var token))
        {
            return Problems.Kobo(new KoboError("kobo.token_unreadable", "The stored API token can no longer be read. A project admin must replace it."));
        }

        var (asset, koboError) = await kobo.VerifyAccessAsync(project.KoboServerUrl, project.KoboAssetUid, token, ct);
        if (koboError is not null)
        {
            return Problems.Kobo(koboError);
        }

        ApplySnapshot(project, asset!, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDetails(project, http.GetProjectRole(), tokenUnreadable: false));
    }

    private static async Task<Ok<List<FormField>>> GetFormFieldsAsync(Guid projectId, TamizaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Projects.Where(p => p.Id == projectId).Select(p => p.FormFields).SingleAsync(ct));

    private static async Task<Ok<WebhookSettings>> GetWebhookAsync(
        Guid projectId, TamizaDbContext db, SecretProtector secrets, IOptions<TamizaOptions> options, CancellationToken ct)
    {
        var encrypted = await db.Projects.Where(p => p.Id == projectId).Select(p => p.EncryptedWebhookSecret).SingleAsync(ct);
        var readable = secrets.TryUnprotect(encrypted, out var secret);
        return TypedResults.Ok(new WebhookSettings(WebhookUrl(options.Value, projectId), WebhookSecret.Username, readable ? secret : null, !readable));
    }

    private static async Task<Ok<WebhookSettings>> RegenerateSecretAsync(
        Guid projectId, TamizaDbContext db, SecretProtector secrets, IOptions<TamizaOptions> options, TimeProvider time, CancellationToken ct)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var secret = WebhookSecret.Generate();
        project.EncryptedWebhookSecret = secrets.Protect(secret);
        project.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new WebhookSettings(WebhookUrl(options.Value, projectId), WebhookSecret.Username, secret, false));
    }

    private static string ValidateName(string? input, FieldErrors errors)
    {
        var name = input?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
        {
            errors.Add("name", $"Enter a name of 1 to {MaxNameLength} characters.");
        }

        return name;
    }

    private static string ValidateToken(string? input, FieldErrors errors)
    {
        var token = input?.Trim() ?? "";
        if (token.Length is 0 or > 200)
        {
            errors.Add("apiToken", "Enter the Kobo API token.");
        }

        return token;
    }

    private static void ApplySnapshot(Project project, KoboAsset asset, DateTimeOffset now)
    {
        project.FormName = asset.Name;
        project.FormFields = asset.Fields.ToList();
        project.FormCheckedAt = now;
        project.UpdatedAt = now;
    }

    private static string WebhookUrl(TamizaOptions options, Guid projectId) =>
        $"{options.PublicUrl.TrimEnd('/')}/api/v1/webhooks/kobo/{projectId}";

    private static ProjectDetails ToDetails(Project project, ProjectRole role, bool tokenUnreadable) => new(
        project.Id,
        project.Name,
        project.KoboServerUrl,
        project.KoboAssetUid,
        project.FormName,
        project.FormCheckedAt,
        project.EnumeratorField,
        FieldCheck.Evaluate(project.FormFields, project.EnumeratorField),
        role,
        tokenUnreadable,
        project.CreatedAt);
}
