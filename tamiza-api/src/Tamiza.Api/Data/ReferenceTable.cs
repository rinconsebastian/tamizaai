namespace Tamiza.Api.Data;

/// <summary>A table an analyst uploaded for the project's scripts (<c>ctx.reference["name"]</c>). Its rows are in <see cref="ReferenceTableContent"/>.</summary>
public sealed class ReferenceTable
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    /// <summary>Lowercase letters, digits and underscores, starting with a letter; unique in the project.</summary>
    public required string Name { get; set; }

    /// <summary>The name of the file the rows came from.</summary>
    public required string FileName { get; set; }

    public List<ReferenceColumn> Columns { get; set; } = [];

    public int RowCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User? UpdatedByUser { get; set; }
}

/// <summary>
/// A column of a reference table: its name as scripts see it, the header it came from, and its type
/// (<c>integer</c>, <c>decimal</c>, <c>date</c> or <c>text</c>).
/// </summary>
public sealed record ReferenceColumn(string Name, string Header, string Type);

/// <summary>The rows of a reference table as a <c>jsonb</c> array of arrays, values in column order.</summary>
public sealed class ReferenceTableContent
{
    public Guid ReferenceTableId { get; set; }

    public required string Rows { get; set; }
}

/// <summary>A reference table as a run read it when it started.</summary>
public sealed record RunReferenceTable(string Name, long Rows, DateTimeOffset UpdatedAt);
