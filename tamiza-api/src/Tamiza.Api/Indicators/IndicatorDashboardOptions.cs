using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Indicators;

/// <summary>
/// Limits of the indicators dashboards, bound from the <c>IndicatorDashboard</c> configuration section
/// (for example <c>IndicatorDashboard__MaxWidgetsPerDashboard</c>). See design D2 and D3. The time limits, query slots and export
/// limits are the clean data dashboard's (<see cref="CleanData.DataDashboardOptions"/>).
/// </summary>
public sealed class IndicatorDashboardOptions
{
    public const string Section = "IndicatorDashboard";

    /// <summary>The most dashboards of its own a project may have. A product rule, not a setting (design D6).</summary>
    public const int MaxDashboards = 7;

    public const string MaxWidgetsPerDashboardVariable = "IndicatorDashboard__MaxWidgetsPerDashboard";

    /// <summary>The variable <see cref="MaxWidgetsPerDashboard"/> replaced, still read when the new one is not set (design D8).</summary>
    public const string MaxWidgetsPerProjectVariable = "IndicatorDashboard__MaxWidgetsPerProject";

    private const string MaxWidgetsPerProjectKey = "MaxWidgetsPerProject";

    /// <summary>The most widgets one dashboard may hold.</summary>
    [Range(1, 500)]
    public int MaxWidgetsPerDashboard { get; set; } = 50;

    /// <summary>The most rows one widget query returns; past it the response says the limit was reached.</summary>
    [Range(1, 100_000)]
    public int MaxWidgetQueryRows { get; set; } = 5000;

    /// <summary>
    /// Whether only the former <c>MaxWidgetsPerProject</c> key is configured, so its value sets <see cref="MaxWidgetsPerDashboard"/>.
    /// A key with any value counts as set, as for the binder: a blank one fails to convert under either name.
    /// </summary>
    public static bool UsesFormerWidgetLimit(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        return section[nameof(MaxWidgetsPerDashboard)] is null && section[MaxWidgetsPerProjectKey] is not null;
    }

    /// <summary>
    /// Applies the former key when <see cref="UsesFormerWidgetLimit"/>. It runs inside the options pipeline, after the section is
    /// bound, so <c>[Range]</c> and <c>ValidateOnStart</c> check the value as if it had been set under the new name.
    /// </summary>
    public static void ApplyFormerWidgetLimit(IndicatorDashboardOptions options, IConfiguration configuration)
    {
        if (UsesFormerWidgetLimit(configuration))
        {
            options.MaxWidgetsPerDashboard = configuration.GetSection(Section).GetValue<int>(MaxWidgetsPerProjectKey);
        }
    }
}
