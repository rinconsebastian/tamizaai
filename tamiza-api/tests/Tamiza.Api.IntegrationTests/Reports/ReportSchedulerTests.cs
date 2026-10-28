using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Reports;

namespace Tamiza.Api.IntegrationTests.Reports;

/// <summary>
/// When scheduled emails go out (design D4 to D6), with the reports' clock set by hand in Bogotá (UTC-5) and the scheduler's passes run
/// by the test. October 2026: Sunday 25, Monday 26, Tuesday 27, Wednesday 28, Thursday 29.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportSchedulerTests(PostgresFixture postgres)
{
    private const int Monday = 1, Thursday = 4, Sunday = 7;

    private static readonly int[] EveryDay = [1, 2, 3, 4, 5, 6, 7];

    private async Task<ReportsHarness> HarnessAsync(bool stat = true)
    {
        var h = await ReportsHarness.CreateAsync(postgres);
        if (stat)
        {
            await h.AddStatAsync();
        }

        return h;
    }

    /// <summary>Runs a pass at a local time in Bogotá and returns how many emails it sent.</summary>
    private static async Task<int> SentAtAsync(ReportsHarness h, int day, int hour, int minute = 0)
    {
        var before = h.Email.Sent.Count;
        h.At(2026, 10, day, hour, minute);
        await h.PassAsync();
        return h.Email.Sent.Count - before;
    }

    private static async Task SaveAtAsync(ReportsHarness h, int day, int hour, int minute = 0, int[]? days = null, string time = "07:00", bool active = true)
    {
        h.At(2026, 10, day, hour, minute);
        await h.SaveReportAsync(days: days ?? EveryDay, time: time, active: active);
    }

    [Fact]
    public async Task Weekday_schedule()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20, days: [Monday, Thursday]);

        Assert.Equal(0, await SentAtAsync(h, 26, 6, 59));
        Assert.Equal(1, await SentAtAsync(h, 26, 7));
        Assert.Equal(0, await SentAtAsync(h, 26, 8));
        Assert.Equal(0, await SentAtAsync(h, 27, 7, 30));
        Assert.Equal(0, await SentAtAsync(h, 28, 7, 30));
        Assert.Equal(1, await SentAtAsync(h, 29, 7));
    }

    [Fact]
    public async Task Every_day()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 26, 10, time: "18:30");

        Assert.Equal(1, await SentAtAsync(h, 26, 18, 30));
        Assert.Equal(0, await SentAtAsync(h, 27, 18, 29));
        Assert.Equal(1, await SentAtAsync(h, 27, 18, 31));
    }

    [Fact]
    public async Task Time_zone_changed()
    {
        await using var h = await HarnessAsync();
        await h.Indicators.ExecuteAsync("UPDATE tamiza.projects SET time_zone = 'UTC' WHERE id = @id", ("id", h.Indicators.ProjectId));
        await SaveAtAsync(h, 25, 20);
        await h.Indicators.ExecuteAsync("UPDATE tamiza.projects SET time_zone = 'America/Bogota' WHERE id = @id", ("id", h.Indicators.ProjectId));

        // 07:00 UTC is 02:00 in Bogotá: nothing yet; then 07:00 in Bogotá.
        h.Clock.Set(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero));
        await h.PassAsync();
        Assert.Empty(h.Email.Sent);
        Assert.Equal(1, await SentAtAsync(h, 26, 7));
    }

    [Fact]
    public async Task Back_the_same_day()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20);

        Assert.Equal(1, await SentAtAsync(h, 26, 9, 5));
        Assert.Equal(0, await SentAtAsync(h, 26, 9, 6));
    }

    [Fact]
    public async Task Down_for_several_days()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20);

        Assert.Equal(1, await SentAtAsync(h, 28, 8));
        Assert.Equal(0, await SentAtAsync(h, 28, 8, 1));
        Assert.Equal(1, await SentAtAsync(h, 29, 7));
    }

    [Fact]
    public async Task Restart_during_a_send()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20);
        h.At(2026, 10, 26, 7);
        // The scheduler takes Monday, then stops before sending.
        await using (var scope = h.Indicators.Factory.Services.CreateAsyncScope())
        {
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ReportQueue>().ClaimDueAsync(h.Clock.GetUtcNow(), CancellationToken.None));
        }

        Assert.Equal(0, await SentAtAsync(h, 26, 7, 2));
        Assert.Equal(1, await SentAtAsync(h, 27, 7));
    }

    [Fact]
    public async Task Paused()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20, active: false);

        Assert.Equal(0, await SentAtAsync(h, 26, 7));
        Assert.Equal(0, await SentAtAsync(h, 27, 7));
    }

    [Fact]
    public async Task Turned_off_then_on_again()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20);
        // The same database served by an api without the SMTP variables, on Monday and Tuesday.
        await using (var off = new TamizaApiFactory(h.Indicators.Db, new Dictionary<string, string?> { ["Reports:SchedulerEnabled"] = "false" }))
        {
            var scheduler = off.Services.GetServices<IHostedService>().OfType<ReportScheduler>().Single();
            Assert.Equal(0, await scheduler.RunPassAsync(CancellationToken.None));
            Assert.Equal(0L, await h.ScalarAsync<long>(
                "SELECT count(*) FROM tamiza.dashboard_reports WHERE dashboard_id = @dashboard AND last_scheduled_at IS NOT NULL"));
        }

        // Restored on Wednesday at 10:00: Wednesday's email once, nothing for Monday or Tuesday, then Thursday's.
        Assert.Equal(1, await SentAtAsync(h, 28, 10));
        Assert.Equal(0, await SentAtAsync(h, 28, 10, 1));
        Assert.Equal(1, await SentAtAsync(h, 29, 7));
    }

    [Fact]
    public async Task A_sunday_only_schedule_follows_the_local_day_around_midnight_utc()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 24, 12, days: [Sunday], time: "20:00");

        // Sunday 20:00 in Bogotá is Monday 01:00 UTC.
        Assert.Equal(1, await SentAtAsync(h, 25, 20));
        Assert.Equal(0, await SentAtAsync(h, 26, 20));
    }

    [Fact]
    public async Task A_monday_schedule_is_not_due_on_sunday_evening_in_bogota()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 24, 12, days: [Monday], time: "20:00");

        Assert.Equal(0, await SentAtAsync(h, 25, 20));
        Assert.Equal(1, await SentAtAsync(h, 26, 20));
    }

    [Fact]
    public async Task A_pause_saved_while_the_claim_runs_wins()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20);
        h.At(2026, 10, 26, 7);
        // A save holds the row while the scheduler looks: the claim skips it, and once the pause is in, nothing is due.
        await using var saver = new NpgsqlConnection(h.Indicators.Db);
        await saver.OpenAsync();
        await using var transaction = await saver.BeginTransactionAsync();
        await using (var pause = new NpgsqlCommand("UPDATE tamiza.dashboard_reports SET active = false", saver, transaction))
        {
            await pause.ExecuteNonQueryAsync();
        }

        await using (var scope = h.Indicators.Factory.Services.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ReportQueue>().ClaimDueAsync(h.Clock.GetUtcNow(), CancellationToken.None));
        }

        await transaction.CommitAsync();
        Assert.Equal(0, await SentAtAsync(h, 26, 7, 1));
    }

    [Fact]
    public async Task Saved_after_the_time()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 26, 10);

        Assert.Equal(0, await SentAtAsync(h, 26, 10, 1));
        Assert.Equal(1, await SentAtAsync(h, 27, 7));
    }

    [Fact]
    public async Task Saved_before_the_time()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 26, 6);

        Assert.Equal(1, await SentAtAsync(h, 26, 7));
    }

    [Fact]
    public async Task Time_corrected_later_the_same_day()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20, time: "15:00");
        await SaveAtAsync(h, 26, 10, time: "07:00");
        Assert.Equal(0, await SentAtAsync(h, 26, 10, 1));

        await SaveAtAsync(h, 26, 10, 30, time: "15:00");

        Assert.Equal(1, await SentAtAsync(h, 26, 15));
    }

    [Fact]
    public async Task Paused_edit_then_activated()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20, active: false);
        await SaveAtAsync(h, 26, 10, active: false);
        await SaveAtAsync(h, 26, 10, 5, time: "15:00");

        Assert.Equal(0, await SentAtAsync(h, 26, 14, 59));
        Assert.Equal(1, await SentAtAsync(h, 26, 15));
    }

    [Fact]
    public async Task Re_saved_after_the_days_email()
    {
        await using var h = await HarnessAsync();
        await SaveAtAsync(h, 25, 20);
        Assert.Equal(1, await SentAtAsync(h, 26, 7));

        await SaveAtAsync(h, 26, 7, 30, time: "08:00");

        Assert.Equal(0, await SentAtAsync(h, 26, 8));
        Assert.Equal(1, await SentAtAsync(h, 27, 8));
    }
}
