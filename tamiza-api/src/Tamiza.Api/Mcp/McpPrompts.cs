using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>Prompts a person can pick in their AI client to start a piece of work.</summary>
[McpServerPromptType]
public sealed class McpPrompts(McpAccessGate gate, TamizaDbContext db)
{
    [McpServerPrompt(Name = "describe_situation", Title = "Describe a situation with the survey data"),
     Description("Starts the work of describing a situation with a project's data: read the form and the data, propose and confirm the indicators, write and test the scripts, run, and build a dashboard.")]
    public async Task<string> DescribeSituation(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("The situation to describe, for example 'access to drinking water by municipality'.")] string situation,
        CancellationToken ct = default)
    {
        await gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
        var name = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Name).SingleAsync(ct);
        return McpGuide.SituationPrompt(name, projectId, situation);
    }
}
