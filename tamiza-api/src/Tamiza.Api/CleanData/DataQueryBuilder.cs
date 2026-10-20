using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.Http;

namespace Tamiza.Api.CleanData;

/// <summary>A value to bind to a command. A parameter object belongs to one command, so queries keep these and create one per command.</summary>
public sealed record SqlParam(string Name, NpgsqlDbType Type, object Value)
{
    public NpgsqlParameter ToParameter() => new(Name, Type) { Value = Value };
}

/// <summary>
/// A request that passed validation, as SQL text. <see cref="Where"/> and <see cref="OrderBy"/> contain only identifiers
/// that the catalog returned (quoted) and parameter names; every value is in <see cref="Parameters"/>.
/// </summary>
public sealed class DataQuery
{
    /// <summary>The conditions joined with AND, or empty when there are no filters. Without the <c>WHERE</c> keyword.</summary>
    public string Where { get; init; } = "";

    /// <summary>The ordering, always ending in <c>ctid</c> so that ties keep a stable order between pages.</summary>
    public string OrderBy { get; init; } = "ctid";

    /// <summary>True when the member chose a column to sort by; otherwise rows keep the order they were written in.</summary>
    public bool HasSort { get; init; }

    public IReadOnlyList<SqlParam> Parameters { get; init; } = [];

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DataQueryBuilder.DefaultPageSize;

    public long Offset => (Page - 1L) * PageSize;

    public string WhereClause => Where.Length == 0 ? "" : " WHERE " + Where;
}

/// <summary>Validates a request against a table's columns and turns it into SQL (design D2).</summary>
public static class DataQueryBuilder
{
    public const int DefaultPageSize = 50;
    public const int MaxFilters = 50;
    public const int MaxInValues = 100;
    public const int MaxTextLength = 500;

    private static readonly int[] PageSizes = [25, 50, 100];

    /// <param name="timeZone">The project's IANA zone, which gives timestamp ranges their days.</param>
    /// <param name="parameterPrefix">
    /// Starts the names of the filters' parameters (<c>f0</c>, <c>f1min</c>), so a second set built for the same command does not
    /// clash. The time zone parameter is always <c>tz</c>: every set of one command uses the same zone.
    /// </param>
    /// <param name="fieldName">The name the filters' errors are keyed under (<c>filters[0]</c>).</param>
    /// <returns>The query, or null when <paramref name="errors"/> has any error.</returns>
    public static DataQuery? Build(
        DataQueryRequest request, IReadOnlyList<TableColumn> columns, string timeZone, FieldErrors errors, string parameterPrefix = "f", string fieldName = "filters")
    {
        var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var parameters = new List<SqlParam>();
        var conditions = new List<string>();

        var filters = request.Filters ?? [];
        if (filters.Count > MaxFilters)
        {
            errors.Add(fieldName, $"At most {MaxFilters} filters are allowed.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < filters.Count && i < MaxFilters; i++)
        {
            var filter = filters[i];
            var field = $"{fieldName}[{i}]";
            if (filter is null || string.IsNullOrEmpty(filter.Column) || !byName.TryGetValue(filter.Column, out var column))
            {
                errors.Add(field, $"The table has no column '{filter?.Column}'.");
                continue;
            }

            if (!seen.Add(column.Name))
            {
                errors.Add(field, $"Column '{column.Name}' has more than one filter.");
                continue;
            }

            var condition = BuildFilter(filter, column, $"{parameterPrefix}{i}", field, timeZone, parameters, errors);
            if (condition is not null)
            {
                conditions.Add(condition);
            }
        }

        var orderBy = "ctid";
        if (request.Sort is { } sort)
        {
            if (!byName.TryGetValue(sort.Column ?? "", out var sortColumn))
            {
                errors.Add("sort", $"The table has no column '{sort.Column}'.");
            }
            else if (sort.Direction is not ("asc" or "desc"))
            {
                errors.Add("sort", "The direction must be 'asc' or 'desc'.");
            }
            else
            {
                orderBy = $"{DataSources.Quote(sortColumn.Name)} {(sort.Direction == "asc" ? "ASC" : "DESC")} NULLS LAST, ctid";
            }
        }

        var pageSize = request.PageSize ?? DefaultPageSize;
        if (!PageSizes.Contains(pageSize))
        {
            errors.Add("pageSize", $"The page size must be {string.Join(", ", PageSizes)}.");
        }

        var page = request.Page ?? 1;
        if (page < 1)
        {
            errors.Add("page", "The page must be 1 or more.");
        }

        if (errors.Any)
        {
            return null;
        }

        return new DataQuery
        {
            Where = string.Join(" AND ", conditions),
            OrderBy = orderBy,
            HasSort = orderBy != "ctid",
            Parameters = parameters,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Escapes backslash, percent and underscore so that a value matches only itself in a LIKE with escape character backslash.</summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static string? BuildFilter(
        DataFilter filter, TableColumn column, string p, string field, string timeZone, List<SqlParam> parameters, FieldErrors errors)
    {
        var name = column.Name;
        var quoted = DataSources.Quote(name);
        var isText = column.Type == DataColumnType.Text;

        switch (filter.Op)
        {
            case "empty":
                return isText ? $"({quoted} IS NULL OR {quoted}::text = '')" : $"{quoted} IS NULL";
            case "notEmpty":
                return isText ? $"({quoted} IS NOT NULL AND {quoted}::text <> '')" : $"{quoted} IS NOT NULL";
            case "contains" or "equals" when isText:
                if (filter.Value is null)
                {
                    errors.Add(field, $"Column '{name}': the {filter.Op} filter needs a value.");
                    return null;
                }

                if (filter.Value.Length > MaxTextLength)
                {
                    errors.Add(field, $"Column '{name}': the value is longer than {MaxTextLength} characters.");
                    return null;
                }

                if (filter.Op == "equals")
                {
                    parameters.Add(new SqlParam(p, NpgsqlDbType.Text, filter.Value));
                    return $"{quoted}::text = @{p}";
                }

                parameters.Add(new SqlParam(p, NpgsqlDbType.Text, EscapeLike(filter.Value)));
                return $"{quoted}::text ILIKE '%' || @{p} || '%' ESCAPE '\\'";
            case "in" when isText || column.Type == DataColumnType.Boolean:
                return BuildIn(filter, column, p, field, parameters, errors);
            case "range" when column.Type is DataColumnType.Integer or DataColumnType.Decimal or DataColumnType.Date or DataColumnType.Timestamp:
                return BuildRange(filter, column, p, field, timeZone, parameters, errors);
            default:
                errors.Add(field, $"Column '{name}' is {Describe(column.Type)} and does not support the filter '{filter.Op}'.");
                return null;
        }
    }

    private static string? BuildIn(DataFilter filter, TableColumn column, string p, string field, List<SqlParam> parameters, FieldErrors errors)
    {
        var values = filter.Values;
        if (values is null || values.Count == 0)
        {
            errors.Add(field, $"Column '{column.Name}': the 'in' filter needs at least one value.");
            return null;
        }

        if (values.Count > MaxInValues)
        {
            errors.Add(field, $"Column '{column.Name}': at most {MaxInValues} values are allowed.");
            return null;
        }

        var quoted = DataSources.Quote(column.Name);
        if (column.Type == DataColumnType.Boolean)
        {
            var flags = new List<bool>();
            foreach (var value in values)
            {
                if (!bool.TryParse(value, out var flag))
                {
                    errors.Add(field, $"Column '{column.Name}': '{value}' is not true or false.");
                    return null;
                }

                flags.Add(flag);
            }

            parameters.Add(new SqlParam(p, NpgsqlDbType.Array | NpgsqlDbType.Boolean, flags.ToArray()));
            return $"{quoted} = ANY(@{p})";
        }

        if (values.Any(v => v is null || v.Length > MaxTextLength))
        {
            errors.Add(field, $"Column '{column.Name}': a value is longer than {MaxTextLength} characters.");
            return null;
        }

        parameters.Add(new SqlParam(p, NpgsqlDbType.Array | NpgsqlDbType.Text, values.ToArray()));
        return $"{quoted}::text = ANY(@{p})";
    }

    private static string? BuildRange(
        DataFilter filter, TableColumn column, string p, string field, string timeZone, List<SqlParam> parameters, FieldErrors errors)
    {
        if (string.IsNullOrEmpty(filter.Min) && string.IsNullOrEmpty(filter.Max))
        {
            errors.Add(field, $"Column '{column.Name}': a range needs a minimum or a maximum.");
            return null;
        }

        var quoted = DataSources.Quote(column.Name);
        var type = column.Type;
        var dbType = type switch
        {
            DataColumnType.Integer => NpgsqlDbType.Bigint,
            DataColumnType.Decimal => NpgsqlDbType.Numeric,
            _ => NpgsqlDbType.Date, // dates, and the days of a timestamp range
        };

        object? min = null, max = null;
        if (!string.IsNullOrEmpty(filter.Min) && !TryParse(type, filter.Min, out min))
        {
            errors.Add(field, $"Column '{column.Name}': '{filter.Min}' is not a valid minimum for {Describe(type)}.");
            return null;
        }

        if (!string.IsNullOrEmpty(filter.Max) && !TryParse(type, filter.Max, out max))
        {
            errors.Add(field, $"Column '{column.Name}': '{filter.Max}' is not a valid maximum for {Describe(type)}.");
            return null;
        }

        if (min is not null && max is not null && ((IComparable)min).CompareTo(max) > 0)
        {
            errors.Add(field, $"Column '{column.Name}': the minimum is greater than the maximum.");
            return null;
        }

        var parts = new List<string>();
        if (min is not null)
        {
            parameters.Add(new SqlParam($"{p}min", dbType, min));
            parts.Add(type == DataColumnType.Timestamp
                ? $"{quoted} >= (@{p}min::date)::timestamp AT TIME ZONE @tz"
                : $"{quoted} >= @{p}min");
        }

        if (max is not null)
        {
            parameters.Add(new SqlParam($"{p}max", dbType, max));
            parts.Add(type == DataColumnType.Timestamp
                ? $"{quoted} < ((@{p}max::date + 1)::timestamp AT TIME ZONE @tz)"
                : $"{quoted} <= @{p}max");
        }

        if (type == DataColumnType.Timestamp && !parameters.Any(x => x.Name == "tz"))
        {
            parameters.Add(new SqlParam("tz", NpgsqlDbType.Text, timeZone));
        }

        return string.Join(" AND ", parts);
    }

    private static bool TryParse(DataColumnType type, string text, out object? value)
    {
        value = null;
        switch (type)
        {
            case DataColumnType.Integer:
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                {
                    value = whole;
                }

                break;
            case DataColumnType.Decimal:
                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    value = number;
                }

                break;
            default:
                if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    value = day;
                }

                break;
        }

        return value is not null;
    }

    internal static string Describe(DataColumnType type) => type switch
    {
        DataColumnType.Integer => "an integer column",
        DataColumnType.Decimal => "a decimal column",
        DataColumnType.Boolean => "a true/false column",
        DataColumnType.Date => "a date column",
        DataColumnType.Timestamp => "a timestamp column",
        _ => "a text column",
    };
}
