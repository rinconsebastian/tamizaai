using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Tamiza.Api.Auth;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Ingestion;
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
        CreateProjectRequest request, TamizaDbContext db, ICurrentUser user, KoboClient kobo, FormSnapshot snapshot, SecretProtector secrets,
        IOptions<TamizaOptions> options, TimeProvider time, IngestionSignal ingestion, CancellationToken ct)
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
        var projectId = Guid.CreateVersion7(now);
        var content = await snapshot.ResolveAsync(projectId, serverUrl!, assetUid, token, asset!, now, ct);
        var project = new Project
        {
            Id = projectId,
            Name = name,
            KoboServerUrl = serverUrl!,
            KoboAssetUid = assetUid,
            EncryptedApiToken = secrets.Protect(token),
            EncryptedWebhookSecret = secrets.Protect(webhookSecret),
            FormName = asset!.Name,
            FormFields = content.Fields.ToList(),
            FormChoices = content.Choices.ToList(),
            FormVersion = content.VersionUid,
            FormCheckedAt = now,
            // The first synchronization back-fills the form's existing submissions right away.
            NextSyncAt = now,
            NextReconcileAt = now.AddHours(24),
            CreatedBy = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        if (content.Version is not null)
        {
            // Nothing else can reach a project that does not exist yet, so the version row is saved with it.
            db.FormVersions.Add(content.Version);
        }

        db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = user.Id, Role = ProjectRole.Admin, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        ingestion.Wake();

        var details = ToDetails(project, ProjectRole.Admin, tokenUnreadable: false, unavailableFormVersions: 0, frameDimensions: null);
        var webhook = new WebhookSettings(WebhookUrl(options.Value, project.Id), WebhookSecret.Username, webhookSecret, false);
        return TypedResults.Created($"/api/v1/projects/{project.Id}", new CreatedProject(details, webhook));
    }

    private static async Task<Ok<ProjectDetails>> GetAsync(
        Guid projectId, HttpContext http, TamizaDbContext db, FormVersionStore versions, SecretProtector secrets, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        return TypedResults.Ok(await DetailsAsync(db, project, http, versions, secrets, ct));
    }

    private static async Task<Results<Ok<ProjectDetails>, ValidationProblem>> UpdateAsync(
        Guid projectId, UpdateProjectRequest request, HttpContext http, TamizaDbContext db, FormVersionStore versions, SecretProtector secrets,
        TimeProvider time, CancellationToken ct)
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

        if (request.TimeZone is not null)
        {
            var zone = request.TimeZone.Trim();
            if (await TimeZoneKnownAsync(db, zone, ct))
            {
                project.TimeZone = zone;
            }
            else
            {
                errors.Add("timeZone", $"'{zone}' is not a time zone name. Use a name such as UTC or America/Bogota.");
            }
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        project.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailsAsync(db, project, http, versions, secrets, ct));
    }

    /// <summary>Whether PostgreSQL knows <paramref name="zone"/>; the database applies the zone, so it is the judge.</summary>
    private static async Task<bool> TimeZoneKnownAsync(TamizaDbContext db, string zone, CancellationToken ct) =>
        zone.Length > 0
        && await db.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = @name) AS \"Value\"", new NpgsqlParameter("name", zone))
            .SingleAsync(ct);

    private static async Task<Results<Ok<ProjectDetails>, ValidationProblem, ProblemHttpResult>> ReplaceTokenAsync(
        Guid projectId, ReplaceTokenRequest request, HttpContext http, TamizaDbContext db, KoboClient kobo, FormSnapshot snapshot, FormVersionStore versions,
        SecretProtector secrets, TimeProvider time, CancellationToken ct)
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

        // A new token gives the versions that failed or were not found a fresh start: the background worker retries them
        // within a tick. Done before the snapshot, so the outcome the snapshot records for the deployed version stands.
        var now = time.GetUtcNow();
        await versions.MakeUnavailableDueAsync(projectId, now, ct);
        project.EncryptedApiToken = secrets.Protect(token);
        await snapshot.ApplyAsync(project, asset!, token, now, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailsAsync(db, project, http, versions, secrets, ct, tokenUnreadable: false));
    }

    private static async Task<Results<Ok<ProjectDetails>, ProblemHttpResult>> RecheckAsync(
        Guid projectId, HttpContext http, TamizaDbContext db, KoboClient kobo, FormSnapshot snapshot, FormVersionStore versions, FormVersionFetcher fetcher,
        IOptions<IngestionOptions> ingestion,
        SecretProtector secrets, TimeProvider time, CancellationToken ct)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        if (!secrets.TryUnprotect(project.EncryptedApiToken, out var token))
        {
            return Problems.Kobo(new KoboError(KoboErrorCodes.TokenUnreadable, "The stored API token can no longer be read. A project admin must replace it."));
        }

        var (asset, koboError) = await kobo.VerifyAccessAsync(project.KoboServerUrl, project.KoboAssetUid, token, ct);
        if (koboError is not null)
        {
            return Problems.Kobo(koboError);
        }

        await snapshot.ApplyAsync(project, asset!, token, time.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);

        // Every version whose definition is failed or not found gets another try. The ones the budget leaves over stay
        // due for the background worker, and the details report how many are still unavailable.
        await fetcher.FetchAsync(project, token, FetchMode.All, ingestion.Value.RecheckVersionBudget, ct);
        return TypedResults.Ok(await DetailsAsync(db, project, http, versions, secrets, ct, tokenUnreadable: false));
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

    private static string WebhookUrl(TamizaOptions options, Guid projectId) =>
        $"{options.PublicUrl.TrimEnd('/')}/api/v1/webhooks/kobo/{projectId}";

    private static async Task<ProjectDetails> DetailsAsync(
        TamizaDbContext db, Project project, HttpContext http, FormVersionStore versions, SecretProtector secrets, CancellationToken ct,
        bool? tokenUnreadable = null) =>
        ToDetails(
            project,
            http.GetProjectRole(),
            tokenUnreadable ?? !secrets.TryUnprotect(project.EncryptedApiToken, out _),
            await versions.CountUnavailableAsync(project.Id, ct),
            // Only the dimensions: the check needs the fields they map to, not the frame's thousands of targets.
            await db.SamplingFrames.AsNoTracking().Where(f => f.ProjectId == project.Id).Select(f => f.Dimensions).SingleOrDefaultAsync(ct));

    private static ProjectDetails ToDetails(
        Project project, ProjectRole role, bool tokenUnreadable, int unavailableFormVersions, IReadOnlyList<SamplingDimension>? frameDimensions) => new(
        project.Id,
        project.Name,
        project.KoboServerUrl,
        project.KoboAssetUid,
        project.FormName,
        project.FormCheckedAt,
        project.EnumeratorField,
        FieldCheck.Evaluate(project.FormFields, project.EnumeratorField, frameDimensions),
        role,
        tokenUnreadable,
        project.CreatedAt,
        unavailableFormVersions,
        project.TimeZone);
}
