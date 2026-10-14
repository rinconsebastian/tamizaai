namespace Tamiza.Api.CleanData;

/// <summary>What the dashboard knows a column as, which decides the filters it offers (camelCase in JSON, through the API's global enum converter).</summary>
public enum DataColumnType
{
    Integer,
    Decimal,
    Boolean,
    Date,
    Timestamp,
    Text,
}

/// <summary>A column of a published table; <paramref name="Label"/> is the question label stored as the column's comment.</summary>
public sealed record TableColumn(string Name, DataColumnType Type, string? Label);

public static class ColumnTypes
{
    /// <summary>Maps PostgreSQL's <c>format_type</c> output. The worker publishes only the types listed here; anything else reads as text.</summary>
    public static DataColumnType FromPostgres(string formatType)
    {
        if (formatType is "bigint" or "integer" or "smallint")
        {
            return DataColumnType.Integer;
        }

        if (formatType.StartsWith("numeric", StringComparison.Ordinal) || formatType is "double precision" or "real")
        {
            return DataColumnType.Decimal;
        }

        if (formatType == "boolean")
        {
            return DataColumnType.Boolean;
        }

        if (formatType == "date")
        {
            return DataColumnType.Date;
        }

        if (formatType.StartsWith("timestamp", StringComparison.Ordinal))
        {
            return DataColumnType.Timestamp;
        }

        return DataColumnType.Text;
    }
}

/// <summary>The most frequent values of a column, most frequent first, ties by value.</summary>
public sealed record DataValueList(IReadOnlyList<DataValueCount> Values);
