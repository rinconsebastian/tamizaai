using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Projects;

/// <summary>Keeps the project's copy of the form current: on re-check, token replacement and every synchronization.</summary>
public static class FormSnapshot
{
    public static void Apply(Project project, KoboAsset asset, DateTimeOffset now)
    {
        project.FormName = asset.Name;
        project.FormFields = asset.Fields.ToList();
        project.FormChoices = asset.Choices.ToList();
        project.FormCheckedAt = now;
        project.UpdatedAt = now;
    }
}
