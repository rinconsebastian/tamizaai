using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Ingestion;

/// <summary>How a fetch pass ended: the versions it took and how many of them have a definition afterwards.</summary>
public readonly record struct FetchResult(int Selected, int Fetched)
{
    public int Unavailable => Selected - Fetched;
}

/// <summary>
/// Fetches the definitions of a project's form versions from Kobo, one request per version, and records the outcome of
/// each. A failure never throws: it becomes the version's status, so callers (synchronizations, the background worker and
/// re-checks) carry on whatever Kobo does. Only cancellation is rethrown.
/// </summary>
public sealed class FormVersionFetcher(FormVersionStore versions, KoboClient kobo, TimeProvider time, ILogger<FormVersionFetcher> logger)
{
    /// <summary>
    /// Fetches the project's versions selected by <paramref name="mode"/>. <paramref name="token"/> is null when the stored
    /// token can no longer be read. With a <paramref name="budget"/>, versions not requested when it runs out are left pending
    /// and due, for the background worker.
    /// </summary>
    public async Task<FetchResult> FetchAsync(Project project, string? token, FetchMode mode, TimeSpan? budget, CancellationToken cancellationToken)
    {
        var uids = await versions.SelectAsync(project.Id, mode, time.GetUtcNow(), cancellationToken);
        if (uids.Count == 0)
        {
            return default;
        }

        var started = time.GetTimestamp();
        var fetched = 0;
        bool? assetReadable = null;
        KoboError? assetError = null;
        for (var i = 0; i < uids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget is { } limit && time.GetElapsedTime(started) >= limit)
            {
                await versions.MakeDueAsync(project.Id, uids.Skip(i).ToList(), time.GetUtcNow(), cancellationToken);
                break;
            }

            var uid = uids[i];
            KoboError? error;
            var serverWide = false;
            if (token is null)
            {
                error = new KoboError(KoboErrorCodes.TokenUnreadable, "The stored API token can no longer be read. A project admin must replace it.");
            }
            else
            {
                var (version, requestError) = await kobo.GetVersionAsync(project.KoboServerUrl, project.KoboAssetUid, uid, token, cancellationToken);
                if (requestError is null)
                {
                    await versions.SaveAvailableAsync(project.Id, uid, version!.Fields, version.Choices, version.DeployedAt, time.GetUtcNow(), cancellationToken);
                    fetched++;
                    continue;
                }

                error = requestError;
                if (error.Code == KoboErrorCodes.VersionNotFound)
                {
                    // Kobo only means "no such version" if it still lets the token read the form. Checked once per pass.
                    if (assetReadable is null)
                    {
                        assetError = (await kobo.GetAssetAsync(project.KoboServerUrl, project.KoboAssetUid, token, cancellationToken)).Error;
                        assetReadable = assetError is null;
                    }

                    if (assetReadable is true)
                    {
                        await versions.SaveNotFoundAsync(project.Id, uid, error, time.GetUtcNow(), cancellationToken);
                        continue;
                    }

                    error = assetError!;
                    serverWide = true;
                }
            }

            if (serverWide || KoboErrorCodes.IsServerWide(error.Code))
            {
                // Every remaining request would fail the same way: an outage costs one timeout, not one per version.
                logger.LogWarning("Fetching form versions of project {ProjectId} stopped: {Code}. {Count} versions marked failed.",
                    project.Id, error.Code, uids.Count - i);
                for (var j = i; j < uids.Count; j++)
                {
                    await versions.SaveFailedAsync(project.Id, uids[j], error, time.GetUtcNow(), cancellationToken);
                }

                break;
            }

            logger.LogWarning("Form version {Version} of project {ProjectId} could not be fetched: {Code}.", uid, project.Id, error.Code);
            await versions.SaveFailedAsync(project.Id, uid, error, time.GetUtcNow(), cancellationToken);
        }

        return new FetchResult(uids.Count, fetched);
    }
}
