using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Indicators;

/// <summary>
/// Limits of the indicators dashboard, bound from the <c>IndicatorDashboard</c> configuration section
/// (for example <c>IndicatorDashboard__MaxWidgetsPerProject</c>). See design D2 and D3. The time limits, query slots and export
/// limits are the clean data dashboard's (<see cref="CleanData.DataDashboardOptions"/>).
/// </summary>
public sealed class IndicatorDashboardOptions
{
    public const string Section = "IndicatorDashboard";

    /// <summary>The most widgets one project's dashboard may hold.</summary>
    [Range(1, 500)]
    public int MaxWidgetsPerProject { get; set; } = 50;

    /// <summary>The most rows one widget query returns; past it the response says the limit was reached.</summary>
    [Range(1, 100_000)]
    public int MaxWidgetQueryRows { get; set; } = 5000;
}
