using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Assistant;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Mcp;

/// <summary>
/// Builds the assistant's <see cref="Masker"/> for the results of tests and runs read at summaries (design D7): the names of the tables,
/// columns, questions and choice lists of the project, the members of <c>ctx</c> and the contract functions are kept, and so are the
/// choice and question labels as whole phrases and the words of the script that failed, which the client can read with get_script.
/// Any other word or number of an error or a log line becomes an ellipsis.
/// </summary>
public sealed class McpMaskerFactory(ScriptInputsReader reader, TamizaDbContext db)
{
    public async Task<Masker> ForTestAsync(Guid projectId, ScriptKind kind, string? indicator, string draft, CancellationToken ct)
    {
        var corpus = draft;
        if (kind != ScriptKind.Clean)
        {
            // An output or indicator test runs the saved script 1 first, and its errors are in that script's words.
            corpus += "\n" + await CurrentCodeAsync(projectId, ScriptKind.Clean, null, ct);
        }

        var (identifiers, labels) = await NamesAsync(projectId, [kind], ct);
        if (indicator is not null)
        {
            identifiers.Add(indicator);
        }

        return new Masker(identifiers, labels, corpus);
    }

    /// <summary>For a run: the words of every script the project has, because any step can fail.</summary>
    public async Task<Masker> ForRunAsync(Guid projectId, CancellationToken ct)
    {
        var codes = await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Code != null
                        && v.Number == db.ScriptVersions.Where(o => o.ProjectId == projectId && o.Kind == v.Kind && o.Name == v.Name).Max(o => o.Number))
            .Select(v => new { v.Name, v.Code })
            .ToListAsync(ct);
        var (identifiers, labels) = await NamesAsync(projectId, [ScriptKind.Clean, ScriptKind.Indicator], ct);
        identifiers.UnionWith(codes.Where(c => c.Name is not null).Select(c => c.Name!));
        return new Masker(identifiers, labels, string.Join("\n", codes.Select(c => c.Code)));
    }

    private async Task<string> CurrentCodeAsync(Guid projectId, ScriptKind kind, string? name, CancellationToken ct) =>
        await db.ScriptVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Kind == kind && v.Name == name)
            .OrderByDescending(v => v.Number)
            .Select(v => v.Code)
            .FirstOrDefaultAsync(ct) ?? "";

    private async Task<(HashSet<string> Identifiers, List<string> Labels)> NamesAsync(Guid projectId, IEnumerable<ScriptKind> kinds, CancellationToken ct)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var labels = new List<string>();
        foreach (var kind in kinds)
        {
            var inputs = await reader.ReadAsync(projectId, kind.ToString().ToLowerInvariant(), ct);
            foreach (var table in inputs.Tables)
            {
                identifiers.Add(table.Name);
                foreach (var column in table.Columns)
                {
                    identifiers.Add(column.Name);
                    if (column.Label is not null)
                    {
                        labels.Add(column.Label);
                    }
                }
            }

            foreach (var reference in inputs.ReferenceTables)
            {
                identifiers.Add(reference.Name);
                identifiers.UnionWith(reference.Columns.Select(c => c.Name));
            }

            foreach (var question in inputs.Questions)
            {
                identifiers.Add(question.Name);
                identifiers.UnionWith(question.Path.Split('/', StringSplitOptions.RemoveEmptyEntries));
                if (question.Label is not null)
                {
                    labels.Add(question.Label);
                }
            }
        }

        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.FormChoices).SingleAsync(ct);
        identifiers.UnionWith(project.Select(c => c.ListName));
        labels.AddRange(project.Select(c => c.Label).Where(l => !string.IsNullOrWhiteSpace(l))!);
        identifiers.UnionWith(ContextAssembler.CtxMembers);
        identifiers.UnionWith(["clean", "prepare_output", "compute"]);
        return (identifiers, labels);
    }
}
