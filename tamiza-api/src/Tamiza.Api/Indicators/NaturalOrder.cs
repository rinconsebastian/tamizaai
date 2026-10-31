using System.Globalization;

namespace Tamiza.Api.Indicators;

/// <summary>
/// The natural order of group and series values: runs of digits compare as numbers, so 2 comes before 10, case and accents are ignored,
/// and a missing value goes last. Twin of <c>naturalCompare</c> in tamiza-ui <c>indicators/chart-scales.ts</c>.
/// </summary>
public static class NaturalOrder
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>Compares two values; a null value (no group, no series) goes after every other.</summary>
    public static int Between(string? x, string? y) => (x, y) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => Compare.Compare(x, y, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace),
    };

    /// <summary>The order of a widget's rows by their group value.</summary>
    public sealed class ByGroup : IComparer<IndicatorRow>
    {
        public static readonly ByGroup Instance = new();

        int IComparer<IndicatorRow>.Compare(IndicatorRow? a, IndicatorRow? b) => Between(a?.Group, b?.Group);
    }

    /// <summary>The order of text values, a null one last.</summary>
    public sealed class ByText : IComparer<string?>
    {
        public static readonly ByText Instance = new();

        int IComparer<string?>.Compare(string? a, string? b) => Between(a, b);
    }
}
