namespace Tamiza.Api.CleanData;

/// <summary>
/// One condition on one column. <c>op</c> is <c>contains</c>, <c>equals</c> or <c>in</c> (text), <c>range</c> (numbers, dates and
/// timestamps), <c>in</c> with <c>true</c>/<c>false</c> (true/false), or <c>empty</c> / <c>notEmpty</c> (any column). Values are
/// always strings and are read as the column's type: <c>2026-09-01</c> for days, <c>.</c> as the decimal separator.
/// </summary>
/// <param name="Value">The text for <c>contains</c> and <c>equals</c>.</param>
/// <param name="Values">The chosen values for <c>in</c>.</param>
/// <param name="Min">The smallest value for <c>range</c>, included. Optional.</param>
/// <param name="Max">The largest value for <c>range</c>, included. Optional; a timestamp range includes the whole last day.</param>
public sealed record DataFilter(string Column, string Op, string? Value = null, IReadOnlyList<string>? Values = null, string? Min = null, string? Max = null);

/// <param name="Direction"><c>asc</c> or <c>desc</c>.</param>
public sealed record DataSort(string Column, string Direction);

/// <param name="Filters">At most one per column; rows must match all of them.</param>
/// <param name="Sort">One column, empty values last. Without it rows keep the order the script published them in.</param>
/// <param name="Page">From 1; 1 by default.</param>
/// <param name="PageSize">25, 50 or 100; 50 by default.</param>
public sealed record DataQueryRequest(IReadOnlyList<DataFilter>? Filters = null, DataSort? Sort = null, int? Page = null, int? PageSize = null);
