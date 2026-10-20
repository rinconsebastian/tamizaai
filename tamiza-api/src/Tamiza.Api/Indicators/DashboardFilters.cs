using System.Globalization;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.Indicators;

/// <summary>
/// The kinds of dashboard filter (design D2), stored as strings: the stored JSON's serializer has no enum converter. A kind matches a
/// family of column types, so a dimension published as an integer by one script and as a decimal by another is filtered the same way.
/// </summary>
public static class DashboardFilterKinds
{
    /// <summary>Text columns; the filter keeps the rows holding one of its values.</summary>
    public const string Text = "text";

    /// <summary>True/false columns; the filter keeps the rows holding one of its values.</summary>
    public const string Boolean = "boolean";

    /// <summary>Integer and decimal columns; the filter keeps the rows inside its range.</summary>
    public const string Number = "number";

    /// <summary>Date and timestamp columns; the filter keeps the rows inside its range of days, in the project's time zone.</summary>
    public const string Date = "date";

    public static readonly IReadOnlyList<string> All = [Text, Boolean, Number, Date];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);

    /// <summary>Whether a filter of this kind applies to a column of this type.</summary>
    public static bool Matches(string kind, DataColumnType type) => kind switch
    {
        Text => type == DataColumnType.Text,
        Boolean => type == DataColumnType.Boolean,
        Number => type is DataColumnType.Integer or DataColumnType.Decimal,
        Date => type is DataColumnType.Date or DataColumnType.Timestamp,
        _ => false,
    };

    /// <summary>The kind a column's type belongs to.</summary>
    public static string Of(DataColumnType type) => type switch
    {
        DataColumnType.Boolean => Boolean,
        DataColumnType.Integer or DataColumnType.Decimal => Number,
        DataColumnType.Date or DataColumnType.Timestamp => Date,
        _ => Text,
    };

    /// <summary>Text and true/false filters choose values; number and date filters take a range.</summary>
    public static bool TakesValues(string kind) => kind is Text or Boolean;

    /// <summary>The clean data dashboard's filter that does what a value of this kind says on <paramref name="column"/>.</summary>
    public static DataFilter ToDataFilter(string column, string kind, DashboardFilterValue value) =>
        TakesValues(kind)
            ? new DataFilter(column, "in", Values: value.Values)
            : new DataFilter(column, "range", Min: Empty(value.Min), Max: Empty(value.Max));

    /// <summary>
    /// The column as the filter is built against it. A number filter compares as a decimal on an integer column too, so a bound
    /// such as <c>2.5</c>, valid for its kind, is not refused; PostgreSQL compares an integer with a numeric bound exactly.
    /// </summary>
    public static TableColumn AsFiltered(string kind, TableColumn column) =>
        kind == Number && column.Type == DataColumnType.Integer ? column with { Type = DataColumnType.Decimal } : column;

    /// <summary>
    /// Checks that <paramref name="value"/> is a value of <paramref name="kind"/>, adding errors on <paramref name="field"/> that
    /// name <paramref name="column"/>. A known kind is assumed; the caller reports an unknown one.
    /// </summary>
    public static void CheckValue(string kind, DashboardFilterValue? value, string column, string field, FieldErrors errors)
    {
        if (value is null)
        {
            errors.Add(field, $"Column '{column}': the filter needs a value.");
            return;
        }

        if (TakesValues(kind))
        {
            CheckValues(kind, value, column, field, errors);
        }
        else
        {
            CheckRange(kind, value, column, field, errors);
        }
    }

    private static void CheckValues(string kind, DashboardFilterValue value, string column, string field, FieldErrors errors)
    {
        if (value.Min is not null || value.Max is not null)
        {
            errors.Add(field, $"Column '{column}': a {kind} filter takes values, not a range.");
            return;
        }

        var values = value.Values;
        if (values is null || values.Count == 0)
        {
            errors.Add(field, $"Column '{column}': choose at least one value.");
        }
        else if (values.Count > DataQueryBuilder.MaxInValues)
        {
            errors.Add(field, $"Column '{column}': at most {DataQueryBuilder.MaxInValues} values are allowed.");
        }
        else if (kind == Boolean && values.FirstOrDefault(v => !bool.TryParse(v, out _)) is { } notFlag)
        {
            errors.Add(field, $"Column '{column}': '{notFlag}' is not true or false.");
        }
        else if (values.Any(v => v is null || v.Length > DataQueryBuilder.MaxTextLength))
        {
            errors.Add(field, $"Column '{column}': a value is longer than {DataQueryBuilder.MaxTextLength} characters.");
        }
    }

    private static void CheckRange(string kind, DashboardFilterValue value, string column, string field, FieldErrors errors)
    {
        if (value.Values is { Count: > 0 })
        {
            errors.Add(field, $"Column '{column}': a {kind} filter takes a range, not values.");
            return;
        }

        var (min, max) = (Empty(value.Min), Empty(value.Max));
        if (min is null && max is null)
        {
            errors.Add(field, $"Column '{column}': a range needs a minimum or a maximum.");
            return;
        }

        IComparable? low = null, high = null;
        if (min is not null && (low = Parse(kind, min)) is null)
        {
            errors.Add(field, $"Column '{column}': '{min}' is not a valid minimum for a {kind} filter.");
            return;
        }

        if (max is not null && (high = Parse(kind, max)) is null)
        {
            errors.Add(field, $"Column '{column}': '{max}' is not a valid maximum for a {kind} filter.");
            return;
        }

        if (low is not null && high is not null && low.CompareTo(high) > 0)
        {
            errors.Add(field, $"Column '{column}': the minimum is greater than the maximum.");
        }
    }

    /// <summary>Reads a bound the way <see cref="DataQueryBuilder"/> reads a decimal or a day, so a checked value is never refused there.</summary>
    private static IComparable? Parse(string kind, string text) =>
        kind == Number
            ? decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null
            : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static string? Empty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}

/// <summary>The limits of a dashboard's filters.</summary>
public static class DashboardFilterRules
{
    /// <summary>The most filters a dashboard has, and so the most a query applies and a widget ignores.</summary>
    public const int MaxFilters = 8;

    public const int MaxLabelLength = 40;
}

/// <summary>One of a dashboard's filters, as stored in <c>dashboards.filters</c> and returned with the dashboard (design D1).</summary>
/// <param name="Column">The column it narrows, in every table that has it with a type of <paramref name="Kind"/>; unique in the dashboard.</param>
/// <param name="Label">What members see, trimmed; unique in the dashboard whatever its case, and never translated.</param>
/// <param name="Kind">One of <see cref="DashboardFilterKinds"/>.</param>
/// <param name="Default">The value in force when a member opens the dashboard without values in its address; null for none.</param>
public sealed record DashboardFilter(string Column, string Label, string Kind, DashboardFilterValue? Default);

/// <summary>A filter as the request to save a dashboard's filters carries it; <see cref="DashboardValidation.Filters"/> checks it.</summary>
public sealed record DashboardFilterInput(string? Column, string? Label, string? Kind, DashboardFilterValue? Default = null);

/// <summary>The body that replaces a dashboard's filters, and the answer: the whole list, in order (design D4).</summary>
public sealed record DashboardFiltersRequest(IReadOnlyList<DashboardFilterInput?>? Filters);

/// <param name="Filters">The dashboard's filters as saved, in order.</param>
public sealed record DashboardFilterList(IReadOnlyList<DashboardFilter> Filters);

/// <summary>
/// A dashboard filter's value in force on a member's screen, sent with a widget's query (design D3): the page sends those the widget
/// does not ignore. It narrows the widget when the table has <see cref="Column"/> with a type of <see cref="Kind"/>.
/// </summary>
public sealed record AppliedDashboardFilter(
    string? Column, string? Kind, IReadOnlyList<string>? Values = null, string? Min = null, string? Max = null)
{
    public DashboardFilterValue Value => new(Values, Min, Max);
}

/// <summary>A value of a dashboard filter: <see cref="Values"/> for a text or true/false filter, a range for a number or date filter.</summary>
/// <param name="Values">1 to 100 values; for a true/false filter, <c>true</c> and/or <c>false</c>.</param>
/// <param name="Min">The smallest value, included: an invariant decimal for a number filter, <c>YYYY-MM-DD</c> for a date filter.</param>
/// <param name="Max">The largest value, included; a date range includes the whole last day.</param>
public sealed record DashboardFilterValue(IReadOnlyList<string>? Values = null, string? Min = null, string? Max = null);
