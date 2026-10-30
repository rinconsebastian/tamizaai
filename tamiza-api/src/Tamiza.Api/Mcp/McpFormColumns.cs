using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Mcp;

/// <summary>How a published column relates to the question of the form it answers.</summary>
/// <param name="Kind"><c>select_one</c> or <c>select_multiple</c> for a select question, otherwise null.</param>
public sealed record McpQuestion(string Question, string Type, string? Kind, string? List, bool OrOther);

/// <summary>Matches the columns of the flattened tables to the questions that produced them, by the column name the flattening gives a question.</summary>
public sealed class McpFormColumns
{
    private readonly Dictionary<string, List<FormField>> _byColumn = new(StringComparer.Ordinal);

    public McpFormColumns(IReadOnlyList<FormField> fields)
    {
        foreach (var field in fields)
        {
            foreach (var key in new[] { ColumnNames.Reduce(field.Name), ColumnNames.Reduce(field.Xpath) }.Distinct(StringComparer.Ordinal))
            {
                if (!_byColumn.TryGetValue(key, out var list))
                {
                    _byColumn[key] = list = [];
                }

                list.Add(field);
            }
        }
    }

    public FormField? FieldOf(string column) =>
        _byColumn.TryGetValue(column, out var candidates)
            ? candidates.FirstOrDefault(f => ColumnNames.Reduce(f.Xpath) == column) ?? candidates[0]
            : null;

    public McpQuestion? QuestionOf(string column)
    {
        var field = FieldOf(column);
        if (field is null)
        {
            return null;
        }

        var kind = IsSelect(field) ? (field.Type.StartsWith("select_multiple", StringComparison.Ordinal) ? "select_multiple" : "select_one") : null;
        return new McpQuestion(field.Xpath, field.Type, kind, kind is null ? null : field.ListName, field.OrOther);
    }

    public static bool IsSelect(FormField field) =>
        field.Type.StartsWith("select_multiple", StringComparison.Ordinal)
        || (field.Type.StartsWith("select_one", StringComparison.Ordinal) && !field.Type.StartsWith("select_one_from_file", StringComparison.Ordinal));
}
