using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tamiza.Api.Email;
using Tamiza.Api.IntegrationTests.Indicators;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Reports;

namespace Tamiza.Api.IntegrationTests.Reports;

/// <summary>
/// The indicators harness with email turned on: messages go to a <see cref="FakeEmailSender"/>, the scheduler sends nothing by itself
/// (tests run its passes), and scheduled emails read a <see cref="SettableTimeProvider"/> while members sign in with the real clock.
/// </summary>
public sealed class ReportsHarness : IAsyncDisposable
{
    /// <summary>Monday 26 October 2026, 10:00 in Bogotá (UTC-5).</summary>
    public static readonly DateTimeOffset Monday10 = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);

    private ReportsHarness(IndicatorsHarness indicators, SettableTimeProvider clock)
    {
        Indicators = indicators;
        Clock = clock;
    }

    public IndicatorsHarness Indicators { get; }

    public SettableTimeProvider Clock { get; }

    public FakeEmailSender Email => (FakeEmailSender)Indicators.Factory.Services.GetRequiredService<IEmailSender>();

    public string ReportUrl(Guid? dashboardId = null) => Indicators.DashboardUrl(dashboardId) + "/report";

    public static async Task<ReportsHarness> CreateAsync(PostgresFixture postgres, Dictionary<string, string?>? settings = null, bool email = true)
    {
        var clock = new SettableTimeProvider(Monday10);
        var all = new Dictionary<string, string?> { ["Reports:SchedulerEnabled"] = "false" };
        if (email)
        {
            all["TAMIZA_SMTP_HOST"] = "smtp.example.org";
            all["TAMIZA_SMTP_FROM"] = "reports@example.org";
        }

        foreach (var (key, value) in settings ?? [])
        {
            all[key] = value;
        }

        var indicators = await IndicatorsHarness.CreateAsync(postgres, all, services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();
            services.RemoveAllKeyed<TimeProvider>(ReportsRegistration.ClockKey);
            services.AddKeyedSingleton<TimeProvider>(ReportsRegistration.ClockKey, clock);
        });
        await indicators.ExecuteAsync("UPDATE tamiza.projects SET time_zone = 'America/Bogota' WHERE id = @id", ("id", indicators.ProjectId));
        await indicators.PublishAsync("households", "municipality text, value numeric", "('Cali', 120), ('Pasto', 80)",
            label: "Households visited", format: "count", unit: "households");
        return new ReportsHarness(indicators, clock);
    }

    /// <summary>A stat tile over the households table on the first dashboard, so that the dashboard has something to send.</summary>
    public Task<JsonElement> AddStatAsync(Guid? dashboardId = null) =>
        Indicators.AddWidgetAsync(new { title = "Total", chartType = "stat", config = new { indicator = "households", measure = "value" } }, dashboardId: dashboardId);

    /// <summary>Saves a dashboard's email as the analyst; days are ISO weekdays.</summary>
    public Task<HttpResponseMessage> PutReportAsync(
        string[]? recipients = null, int[]? days = null, string time = "07:00", bool active = true, string locale = "en-US",
        HttpClient? client = null, Guid? dashboardId = null) =>
        (client ?? Indicators.Analyst).PutAsJsonAsync(ReportUrl(dashboardId), new
        {
            recipients = recipients ?? ["ana@partner.org", "luis@city.gov.co"],
            days = days ?? [1, 2, 3, 4, 5, 6, 7],
            time,
            active,
            locale,
        });

    public async Task SaveReportAsync(
        string[]? recipients = null, int[]? days = null, string time = "07:00", bool active = true, string locale = "en-US", Guid? dashboardId = null)
    {
        var response = await PutReportAsync(recipients, days, time, active, locale, dashboardId: dashboardId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Sets the reports' clock to a local time in Bogotá.</summary>
    public void At(int year, int month, int day, int hour, int minute = 0) =>
        Clock.Set(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(-5)));

    /// <summary>Runs one pass of the scheduler at the reports' clock, as its tick would.</summary>
    public async Task<int> PassAsync()
    {
        var scheduler = Indicators.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReportScheduler>().Single();
        return await scheduler.RunPassAsync(CancellationToken.None);
    }

    public Task<T?> ScalarAsync<T>(string sql) =>
        TestData.ScalarAsync<T>(Indicators.Db, sql, ("dashboard", Indicators.DashboardId));

    public ValueTask DisposeAsync() => Indicators.DisposeAsync();
}
