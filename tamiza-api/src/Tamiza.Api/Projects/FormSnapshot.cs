using Tamiza.Api.Data;
using Tamiza.Api.Ingestion;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

/// <summary>
/// What the project's snapshot describes: the fields and choice lists of the form's deployed version, and that version's
/// uid (null when the form has no deployment). <see cref="Version"/> is the row to keep for the deployed version when this
/// read produced one: its definition, or the reason it could not be read.
/// </summary>
public sealed record FormSnapshotContent(IReadOnlyList<FormField> Fields, IReadOnlyList<FormChoice> Choices, string? VersionUid, FormVersion? Version);

/// <summary>Keeps the project's copy of the form current: on creation, re-check, token replacement and every synchronization.</summary>
public sealed class FormSnapshot(KoboClient kobo, FormVersionStore versions, ILogger<FormSnapshot> logger)
{
    /// <summary>
    /// Picks the content that describes the form's deployed version, from an asset that was just read. The asset's own
    /// <c>content</c> is its latest version, which may hold edits nobody deployed. In order:
    /// <list type="number">
    /// <item>no deployment: the asset's content;</item>
    /// <item>the deployed version is the latest: the asset's content, kept as that version's definition;</item>
    /// <item>the deployed version is already stored: its stored definition, with no request to Kobo;</item>
    /// <item>otherwise: the version fetched from Kobo, or, if that fails, the asset's content with the failure recorded.</item>
    /// </list>
    /// Nothing is written here; <see cref="FormSnapshotContent.Version"/> is for the caller to store.
    /// </summary>
    public async Task<FormSnapshotContent> ResolveAsync(
        Guid projectId, string serverUrl, string assetUid, string token, KoboAsset asset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (asset.DeployedVersionId is not { } deployed)
        {
            return new FormSnapshotContent(asset.Fields, asset.Choices, null, null);
        }

        if (deployed == asset.VersionId)
        {
            return new FormSnapshotContent(asset.Fields, asset.Choices, deployed, Available(projectId, deployed, asset.Fields, asset.Choices, null, now));
        }

        if (await versions.FindAvailableAsync(projectId, deployed, cancellationToken) is { } stored)
        {
            return new FormSnapshotContent(stored.Fields, stored.Choices, deployed, null);
        }

        var (version, error) = await kobo.GetVersionAsync(serverUrl, assetUid, deployed, token, cancellationToken);
        if (error is null)
        {
            return new FormSnapshotContent(version!.Fields, version.Choices, deployed, Available(projectId, deployed, version.Fields, version.Choices, version.DeployedAt, now));
        }

        // The asset itself was just read, so a 404 means Kobo does not know the version, not that the form is gone.
        logger.LogWarning("The deployed version {Version} of project {ProjectId} could not be read ({Code}); the snapshot uses the form's latest content.",
            deployed, projectId, error.Code);
        return new FormSnapshotContent(asset.Fields, asset.Choices, deployed, Unavailable(projectId, deployed, error, now));
    }

    /// <summary>
    /// Takes the snapshot of an existing project: stores the deployed version's row first, in its own short transaction,
    /// then sets the project's fields. The caller saves the project. This order keeps the snapshot from waiting, while
    /// holding the project row, on a <c>form_versions</c> row a webhook transaction has not committed yet.
    /// </summary>
    public async Task ApplyAsync(Project project, KoboAsset asset, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var content = await ResolveAsync(project.Id, project.KoboServerUrl, project.KoboAssetUid, token, asset, now, cancellationToken);
        if (content.Version is not null)
        {
            await versions.SaveAsync(content.Version, cancellationToken);
        }

        Set(project, asset.Name, content, now);
    }

    public static void Set(Project project, string formName, FormSnapshotContent content, DateTimeOffset now)
    {
        project.FormName = formName;
        project.FormFields = content.Fields.ToList();
        project.FormChoices = content.Choices.ToList();
        project.FormVersion = content.VersionUid;
        project.FormCheckedAt = now;
        project.UpdatedAt = now;
    }

    private static FormVersion Available(
        Guid projectId, string uid, IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, DateTimeOffset? deployedAt, DateTimeOffset now) => new()
    {
        ProjectId = projectId,
        Uid = uid,
        Status = FormVersionStatus.Available,
        Fields = fields.ToList(),
        Choices = choices.ToList(),
        DeployedAt = deployedAt,
        RecordedAt = now,
        FetchedAt = now,
    };

    private static FormVersion Unavailable(Guid projectId, string uid, KoboError error, DateTimeOffset now)
    {
        var notFound = error.Code == KoboErrorCodes.VersionNotFound;
        return new FormVersion
        {
            ProjectId = projectId,
            Uid = uid,
            Status = notFound ? FormVersionStatus.NotFound : FormVersionStatus.Failed,
            RecordedAt = now,
            Attempts = notFound ? 0 : 1,
            NextAttemptAt = notFound ? null : now + FormVersionStore.RetryDelay(1),
            ErrorCode = error.Code,
            Error = error.Message,
        };
    }
}
