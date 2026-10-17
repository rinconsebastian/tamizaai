using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Assistant;

/// <summary>What <see cref="SchemaBlock"/> counted while it wrote the block, so the trimmer can say what it left out.</summary>
internal sealed record SchemaStats(int Lists, int ListsShown, int Tables, int TablesShown, int LabelsCut);

/// <summary>The <c>&lt;schema&gt;</c> block, with the names and labels it holds: the masker's allow-list and protected phrases.</summary>
internal sealed record SchemaResult(string Text, IReadOnlySet<string> Identifiers, IReadOnlyList<string> Labels, SchemaStats Stats);

/// <summary>
/// Writes the <c>&lt;schema&gt;</c> block (design D4): for each input table a line with its name and row count and one line per column
/// (type, pandas dtype when it is not obvious, the question label, the choice list), the reference tables with their columns, and the
/// choice lists of the select questions that have columns. With nothing published it lists the form's questions and choice lists.
/// </summary>
internal static partial class SchemaBlock
{
    /// <summary>The most choices of a list that are listed; the rest are counted.</summary>
    public const int MaxChoices = 50;

    public static SchemaResult Render(ContextSources sources, string kind, ContextTrim trim, IReadOnlySet<string> mentions)
    {
        var writer = new Writer(sources, trim, mentions);
        return sources.Inputs.Tables.Count > 0 ? writer.Published(kind) : writer.NothingPublished();
    }

    private sealed class Writer(ContextSources sources, ContextTrim trim, IReadOnlySet<string> mentions)
    {
        private readonly StringBuilder text = new();
        private readonly HashSet<string> identifiers = new(StringComparer.Ordinal);
        private readonly List<string> labels = [];
        private int labelsCut;

        private readonly Dictionary<string, List<FormField>> fieldsByColumn = FieldsByColumn(sources.Fields);

        public SchemaResult Published(string kind)
        {
            var inputs = sources.Inputs;
            var source = kind == "clean" ? "flattened (raw)" : "clean";
            var variable = kind == "clean" ? "raw" : "cleaned";
            var published = inputs.PublishedAt is { } at ? $" published {at.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}" : "";
            text.Append($"Input tables: the {source} tables of the last run,{published}. A script receives them in `{variable}`, a dictionary of DataFrames by table name.\n");

            var included = inputs.Tables
                .Where(t => !trim.UnmentionedTables || t.Name == "main" || mentions.Contains(Lower(t.Name)))
                .ToList();
            var tableNames = inputs.Tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var listFields = new List<FormField>();
            foreach (var table in included)
            {
                WriteTable(table, tableNames, listFields);
            }

            WriteReferenceTables();

            var lists = listFields.Where(f => f.ListName is not null).GroupBy(f => f.ListName!, StringComparer.Ordinal).ToList();
            var shown = trim.UnmentionedLists ? lists.Where(list => IsMentioned(list.Key, list)).ToList() : lists;
            WriteChoiceLists(shown.Select(list => list.Key).ToList());

            var totalLists = inputs.Tables
                .SelectMany(t => t.Columns.Select(c => FieldOf(c.Name)))
                .Where(f => f?.ListName is not null).Select(f => f!.ListName!).Distinct(StringComparer.Ordinal).Count();
            return Result(new SchemaStats(totalLists, shown.Count, inputs.Tables.Count, included.Count, labelsCut));
        }

        public SchemaResult NothingPublished()
        {
            text.Append("No input tables are known yet: the project has not run, so the tables a script receives cannot be listed. ")
                .Append("The form's questions follow (name, type, label, choice list); the answers become columns of the tables.\n");
            var questions = sources.Fields;
            foreach (var field in questions)
            {
                identifiers.Add(field.Name);
                text.Append("  ").Append(field.Name).Append(": ").Append(field.Type);
                AppendLabel(field.Label);
                AppendMarkers(field);
                if (field.InRepeat)
                {
                    text.Append(" [in a repeat group]");
                }

                text.Append('\n');
            }

            WriteReferenceTables();

            var lists = questions.Where(f => f.ListName is not null && IsSelect(f)).GroupBy(f => f.ListName!, StringComparer.Ordinal).ToList();
            var shown = trim.UnmentionedLists ? lists.Where(list => IsMentioned(list.Key, list)).ToList() : lists;
            WriteChoiceLists(shown.Select(list => list.Key).ToList());
            return Result(new SchemaStats(lists.Count, shown.Count, 0, 0, labelsCut));
        }

        private SchemaResult Result(SchemaStats stats) =>
            new(text.ToString().TrimEnd('\n'), identifiers, labels, stats);

        private void WriteTable(InputTable table, HashSet<string> tableNames, List<FormField> listFields)
        {
            identifiers.Add(table.Name);
            var rows = table.Rows is { } count ? $"{count.ToString("N0", CultureInfo.InvariantCulture)} rows" : "row count unknown";
            text.Append('\n').Append(table.Name).Append(": ").Append(rows);
            if (table.Name != "main")
            {
                text.Append(". ").Append(RepeatNote(table.Name, tableNames));
            }

            text.Append('\n');
            foreach (var column in table.Columns)
            {
                identifiers.Add(column.Name);
                text.Append("  ").Append(column.Name).Append(": ").Append(TypeText(column.Type));
                AppendLabel(column.Label);
                var field = FieldOf(column.Name);
                if (field is not null)
                {
                    AppendMarkers(field);
                    if (IsSelect(field))
                    {
                        listFields.Add(field);
                    }
                }

                text.Append('\n');
            }
        }

        /// <summary>
        /// A repeat table's keys. The enclosing repeat is read from the paths of the form's questions: the repeat group is the path
        /// segment that names the table, and the nearest enclosing table's segment before it is its parent.
        /// </summary>
        private string RepeatNote(string table, HashSet<string> tableNames)
        {
            var parent = ParentOf(table, tableNames);
            return parent switch
            {
                "main" => "A repeat table, one row per repeat answer: `_parent_id` is the `_id` of the submission in `main`, and `_parent_table` is `main`.",
                not null => $"A nested repeat table, one row per repeat answer: `_parent_id` is the `_row_id` of the row of `{parent}` it sits in, and `_parent_table` names `{parent}`.",
                _ => "A repeat table, one row per repeat answer: `_parent_id` is the `_id` of the submission in `main` or, for a nested repeat, the `_row_id` of the enclosing repeat row, and `_parent_table` names that table.",
            };
        }

        private string? ParentOf(string table, HashSet<string> tableNames)
        {
            foreach (var field in sources.Fields.Where(f => f.InRepeat))
            {
                var segments = field.Xpath.Split('/');
                var position = Array.FindIndex(segments, segment => segment.Length > 0 && ColumnNames.Reduce(segment) == table);
                if (position < 0 || position == segments.Length - 1)
                {
                    continue;
                }

                for (var before = position - 1; before >= 0; before--)
                {
                    var name = ColumnNames.Reduce(segments[before]);
                    if (tableNames.Contains(name) && name != "main")
                    {
                        return name;
                    }
                }

                return "main";
            }

            return null;
        }

        private void WriteReferenceTables()
        {
            if (sources.Inputs.ReferenceTables.Count == 0)
            {
                return;
            }

            text.Append("\nReference tables (columns and types only; a script reads one with `ctx.reference[\"name\"]`):\n");
            foreach (var table in sources.Inputs.ReferenceTables)
            {
                identifiers.Add(table.Name);
                foreach (var column in table.Columns)
                {
                    identifiers.Add(column.Name);
                }

                text.Append("  ").Append(table.Name).Append(": ")
                    .Append(string.Join(", ", table.Columns.Select(c => $"{c.Name} ({c.Type})"))).Append('\n');
            }
        }

        private void WriteChoiceLists(IReadOnlyList<string> listNames)
        {
            if (listNames.Count == 0)
            {
                return;
            }

            text.Append("\nChoice lists of the select questions (a select answer is stored as one of these labels; older form versions may have used other labels, which `ctx.form.name` resolves):\n");
            foreach (var listName in listNames)
            {
                identifiers.Add(listName);
                var choices = sources.Choices.Where(c => c.ListName == listName).Select(c => Clean(c.Label ?? c.Name)).ToList();
                var shown = choices.Take(MaxChoices).ToList();
                labels.AddRange(shown);
                var rest = choices.Count - shown.Count;
                text.Append("  ").Append(listName).Append(": ").Append(string.Join(" | ", shown));
                if (rest > 0)
                {
                    text.Append($" (and {rest.ToString(CultureInfo.InvariantCulture)} more)");
                }

                text.Append('\n');
            }
        }

        private void AppendLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            var cleaned = Clean(label);
            if (trim.ShortLabels && cleaned.Length > ContextAssembler.LongLabel)
            {
                // The cut label is what the request holds; the phrase the masker protects is the part that is shown.
                var cut = cleaned[..ContextAssembler.LongLabel].TrimEnd();
                labelsCut++;
                labels.Add(cut);
                text.Append(" \"").Append(cut).Append("…\"");
                return;
            }

            labels.Add(cleaned);
            text.Append(" \"").Append(cleaned).Append('"');
        }

        private void AppendMarkers(FormField field)
        {
            if (IsSelect(field))
            {
                var kind = field.Type.StartsWith("select_multiple", StringComparison.Ordinal) ? "select_multiple" : "select_one";
                text.Append(" [").Append(kind).Append(field.ListName is null ? "" : " " + field.ListName).Append(']');
            }

            if (field.OrOther)
            {
                text.Append(" [or other]");
            }
        }

        /// <summary>A list is mentioned when the draft or the messages name the list, a question that uses it, or a column of such a question.</summary>
        private bool IsMentioned(string listName, IEnumerable<FormField> fields) =>
            mentions.Contains(Lower(listName))
            || fields.Any(f => mentions.Contains(Lower(f.Name)) || mentions.Contains(Lower(ColumnNames.Reduce(f.Name))));

        private FormField? FieldOf(string column) =>
            fieldsByColumn.TryGetValue(column, out var candidates)
                ? candidates.FirstOrDefault(f => ColumnNames.Reduce(f.Xpath) == column) ?? candidates[0]
                : null;
    }

    private static Dictionary<string, List<FormField>> FieldsByColumn(IReadOnlyList<FormField> fields)
    {
        var map = new Dictionary<string, List<FormField>>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            foreach (var key in new[] { ColumnNames.Reduce(field.Name), ColumnNames.Reduce(field.Xpath) }.Distinct(StringComparer.Ordinal))
            {
                if (!map.TryGetValue(key, out var list))
                {
                    map[key] = list = [];
                }

                list.Add(field);
            }
        }

        return map;
    }

    private static bool IsSelect(FormField field) =>
        field.Type.StartsWith("select_multiple", StringComparison.Ordinal)
        || (field.Type.StartsWith("select_one", StringComparison.Ordinal) && !field.Type.StartsWith("select_one_from_file", StringComparison.Ordinal));

    private static string TypeText(DataColumnType type) => type switch
    {
        DataColumnType.Integer => "integer (Int64)",
        DataColumnType.Decimal => "decimal (float64)",
        DataColumnType.Boolean => "boolean",
        DataColumnType.Date => "date",
        DataColumnType.Timestamp => "timestamp (datetime64, UTC)",
        _ => "text",
    };

    /// <summary>A label on one line, with its whitespace normalized, as the masker compares it.</summary>
    private static string Clean(string label) => Whitespace().Replace(label, " ").Trim();

    private static string Lower(string name) => name.ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
