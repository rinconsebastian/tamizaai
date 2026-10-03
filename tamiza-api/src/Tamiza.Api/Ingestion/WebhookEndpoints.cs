using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;

namespace Tamiza.Api.Ingestion;

/// <summary>Kobo REST Service intake. Stores the submission and answers; nothing else runs on a webhook.</summary>
public static class WebhookEndpoints
{
    public const string UnsupportedMediaType = "webhook.unsupported_media_type";
    public const string InvalidSubmission = "webhook.invalid_submission";
    public const string TooLarge = "webhook.too_large";

    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder api)
    {
        // Kobo authenticates with HTTP Basic, not with a Keycloak token (see WebhookCredentials).
        api.MapPost("/webhooks/kobo/{projectId:guid}", ReceiveAsync)
            .AllowAnonymous()
            .WithTags("Webhooks")
            .WithName("ReceiveKoboSubmission");
        return api;
    }

    private static async Task<Results<Ok, ProblemHttpResult>> ReceiveAsync(
        Guid projectId, HttpContext http, WebhookCredentials credentials, SubmissionStore store, TamizaDbContext db,
        TimeProvider time, IOptions<IngestionOptions> options, ILoggerFactory loggers, CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(WebhookEndpoints));

        // Credentials first: nothing of the body is read for an unauthenticated request.
        if (!await credentials.AreValidAsync(projectId, http.Request.Headers.Authorization, ct))
        {
            logger.LogInformation("Webhook delivery for project {ProjectId} rejected: invalid credentials.", projectId);
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"tamiza\"";
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid webhook credentials.");
        }

        if (!http.Request.HasJsonContentType())
        {
            logger.LogInformation("Webhook delivery for project {ProjectId} rejected: content type {ContentType}.", projectId, http.Request.ContentType);
            return Problem(StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaType,
                "Kobo's REST Service must send JSON: edit the service in Kobo and choose the JSON type.");
        }

        var body = await ReadLimitedAsync(http.Request, options.Value.MaxWebhookBytes, ct);
        if (body is null)
        {
            logger.LogInformation("Webhook delivery for project {ProjectId} rejected: body over {Limit} bytes.", projectId, options.Value.MaxWebhookBytes);
            return Problem(StatusCodes.Status413PayloadTooLarge, TooLarge,
                $"The submission is larger than {options.Value.MaxWebhookBytes / (1024 * 1024)} MB.");
        }

        SubmissionRow? row;
        string? error;
        try
        {
            using var document = JsonDocument.Parse(body);
            (row, error) = KoboSubmission.Parse(document.RootElement);
        }
        catch (JsonException)
        {
            (row, error) = (null, "The body is not valid JSON.");
        }

        if (row is null)
        {
            logger.LogInformation("Webhook delivery for project {ProjectId} rejected: {Reason}", projectId, error);
            return Problem(StatusCodes.Status400BadRequest, InvalidSubmission, error!);
        }

        var now = time.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await store.UpsertAsync(projectId, [row], SubmissionOrigin.Webhook, now, ct);
            await db.Projects.Where(p => p.Id == projectId).ExecuteUpdateAsync(p => p.SetProperty(x => x.LastWebhookAt, now), ct);
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Ok();
    }

    /// <summary>The whole body, or null when it exceeds <paramref name="limit"/> bytes (declared or actually sent).</summary>
    private static async Task<byte[]?> ReadLimitedAsync(HttpRequest request, long limit, CancellationToken cancellationToken)
    {
        if (request.ContentLength > limit)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ProblemHttpResult Problem(int status, string code, string detail) =>
        TypedResults.Problem(statusCode: status, title: "The webhook delivery was rejected.", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
