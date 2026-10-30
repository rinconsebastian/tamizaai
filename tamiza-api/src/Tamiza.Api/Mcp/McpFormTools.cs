using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Data;
using Tamiza.Api.Ingestion;
using Tamiza.Api.Kobo;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>The Kobo form: what was asked and how it was answered.</summary>
[McpServerToolType]
public sealed class McpFormTools(McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db)
    : McpToolBase(gate, call, options)
{
    /// <summary>The choices of a list shown when the list is not asked for by name.</summary>
    private const int ChoicesPerList = 25;

    [McpServerTool(Name = "get_form", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get the Kobo form"),
     Description("The project's Kobo form from its latest deployed version: every question with its name, path (xpath), type, label, choice list and whether it is in a repeat group, " +
                 "the choice lists with their codes and labels, the form versions the stored submissions use with how many submissions each has, the enumerator question and which performance metrics the form supports. " +
                 "Answers to select questions appear in the data as the choice LABELS, not codes. Large choice lists are shortened: pass list=<list name> to read one in full.")]
    public Task<CallToolResult> GetForm(
        [Description("The project's id, from list_projects.")] Guid projectId,
        [Description("Optional. The name of one choice list to read in full.")] string? list = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
            var versions = McpResults.Unwrap<FormVersionList>(await IngestionEndpoints.ListFormVersionsAsync(projectId, db, ct));
            var sampling = await db.SamplingFrames.AsNoTracking().Where(f => f.ProjectId == projectId).Select(f => f.Dimensions).SingleOrDefaultAsync(ct);
            var lists = project.FormChoices
                .GroupBy(c => c.ListName, StringComparer.Ordinal)
                .Select(g => (Name: g.Key, Choices: g.ToList()))
                .ToList();

            if (list is not null)
            {
                var found = lists.FirstOrDefault(l => string.Equals(l.Name, list, StringComparison.Ordinal));
                if (found.Name is null)
                {
                    throw McpRefusal.NotFound($"The choice list '{list}'");
                }

                return new { name = project.FormName, choiceList = new { name = found.Name, total = found.Choices.Count, choices = found.Choices.Select(c => new { code = c.Name, label = c.Label }).ToList() } };
            }

            return new
            {
                name = project.FormName,
                version = project.FormVersion,
                enumeratorField = project.EnumeratorField,
                fields = project.FormFields.Select(f => new { f.Name, f.Xpath, f.Type, f.Label, list = f.ListName, f.OrOther, f.InRepeat }).ToList(),
                choiceLists = lists.Select(l => new
                {
                    name = l.Name,
                    total = l.Choices.Count,
                    choices = l.Choices.Take(ChoicesPerList).Select(c => new { code = c.Name, label = c.Label }).ToList(),
                    shortened = l.Choices.Count > ChoicesPerList ? $"{l.Choices.Count - ChoicesPerList} more; call get_form with list=\"{l.Name}\"" : null,
                }).ToList(),
                formVersions = versions,
                metrics = FieldCheck.Evaluate(project.FormFields, project.EnumeratorField, sampling),
            };
        }, hint: "Pass list=<choice list name> to read one list in full.");
}
