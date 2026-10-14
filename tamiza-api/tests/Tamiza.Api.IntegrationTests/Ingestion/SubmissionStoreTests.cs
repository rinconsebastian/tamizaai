using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tamiza.Api.Data;
using Tamiza.Api.Ingestion;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.DbUp;

namespace Tamiza.Api.IntegrationTests.Ingestion;

[Collection(PostgresCollection.Name)]
public sealed class SubmissionStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static SubmissionRow Row(long id, string uuid = "u", string json = "{}") =>
        new(id, $"{uuid}-{id}", "v1", T0.AddDays(-1), json);

    private async Task<(string Db, Guid ProjectId)> SeedAsync()
    {
        var db = await postgres.CreateDatabaseAsync();
        await SchemaMigrator.MigrateAsync(db, NullLogger.Instance);
        var userId = Guid.CreateVersion7();
        await TestData.ExecuteAsync(db, "INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES (@id, 'sub', 'Ada', now(), now())", ("id", userId));
        return (db, await TestData.InsertProjectAsync(db, userId));
    }

    private static TamizaDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TamizaDbContext>();
        DatabaseRegistration.Configure(options, connectionString);
        return new TamizaDbContext(options.Options);
    }

    private static async Task<UpsertCounts> UpsertAsync(string db, Guid projectId, SubmissionOrigin origin, DateTimeOffset now, params SubmissionRow[] rows)
    {
        await using var context = Context(db);
        return await new SubmissionStore(context).UpsertAsync(projectId, rows, origin, now, CancellationToken.None);
    }

    private static async Task<int> MarkAsync(string db, Guid projectId, long[] ids, DateTimeOffset listedFrom)
    {
        await using var context = Context(db);
        return await new SubmissionStore(context).MarkDeletedAsync(projectId, ids, listedFrom, listedFrom.AddMinutes(5), CancellationToken.None);
    }

    private static async Task<RawSubmission> GetAsync(string db, Guid projectId, long koboId)
    {
        await using var context = Context(db);
        return await context.RawSubmissions.AsNoTracking().SingleAsync(s => s.ProjectId == projectId && s.KoboId == koboId);
    }

    [Fact]
    public async Task New_rows_count_as_created_changed_as_updated_and_identical_as_neither()
    {
        var (db, projectId) = await SeedAsync();
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(1, json: """{"a": 1, "b": 2}"""), Row(2, json: """{"a": 1}"""));

        var counts = await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0.AddHours(1),
            Row(1, json: """{ "b": 2,  "a": 1 }"""),
            Row(2, json: """{"a": 2}"""),
            Row(3));

        Assert.Equal(new UpsertCounts(Created: 1, Updated: 1), counts);
        Assert.Equal(T0, (await GetAsync(db, projectId, 1)).UpdatedAt);
        Assert.Equal(T0.AddHours(1), (await GetAsync(db, projectId, 2)).UpdatedAt);
        Assert.Equal(3L, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.raw_submissions"));
    }

    [Fact]
    public async Task Updates_keep_origin_and_first_reception_and_record_the_new_uuid()
    {
        var (db, projectId) = await SeedAsync();
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(7, uuid: "first", json: """{"v": 1}"""));

        await UpsertAsync(db, projectId, SubmissionOrigin.Webhook, T0.AddHours(2), Row(7, uuid: "edited", json: """{"v": 2}"""));

        var stored = await GetAsync(db, projectId, 7);
        Assert.Equal(SubmissionOrigin.Sync, stored.Origin);
        Assert.Equal(T0, stored.ReceivedAt);
        Assert.Equal(T0.AddHours(2), stored.UpdatedAt);
        Assert.Equal("edited-7", stored.KoboUuid);
        Assert.Contains("2", stored.Data);
    }

    [Fact]
    public async Task A_deleted_row_received_again_is_restored_and_counted_as_updated()
    {
        var (db, projectId) = await SeedAsync();
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(1));
        await MarkAsync(db, projectId, [], T0.AddHours(1));

        var counts = await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0.AddHours(2), Row(1));

        Assert.Equal(new UpsertCounts(0, 1), counts);
        var stored = await GetAsync(db, projectId, 1);
        Assert.False(stored.Deleted);
        Assert.Null(stored.DeletedAt);
    }

    [Fact]
    public async Task The_last_copy_of_a_repeated_id_in_one_batch_wins()
    {
        var (db, projectId) = await SeedAsync();

        var counts = await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(1, json: """{"v": 1}"""), Row(1, json: """{"v": 2}"""));

        Assert.Equal(new UpsertCounts(1, 0), counts);
        Assert.Contains("2", (await GetAsync(db, projectId, 1)).Data);
    }

    [Fact]
    public async Task Ids_missing_from_kobo_are_marked_deleted_but_late_arrivals_and_other_projects_are_kept()
    {
        var (db, projectId) = await SeedAsync();
        var userId = await TestData.ScalarAsync<Guid>(db, "SELECT id FROM tamiza.users");
        var otherProject = await TestData.InsertProjectAsync(db, userId, "Other");
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(1), Row(2), Row(3));
        await UpsertAsync(db, otherProject, SubmissionOrigin.Sync, T0, Row(2));
        // Arrives by webhook after the listing started; its _id was past the listing's position.
        await UpsertAsync(db, projectId, SubmissionOrigin.Webhook, T0.AddHours(1).AddMinutes(1), Row(4));

        var marked = await MarkAsync(db, projectId, [1, 3], listedFrom: T0.AddHours(1));

        Assert.Equal(1, marked);
        var two = await GetAsync(db, projectId, 2);
        Assert.True(two.Deleted);
        Assert.Equal(T0.AddHours(1).AddMinutes(5), two.DeletedAt);
        Assert.False((await GetAsync(db, projectId, 4)).Deleted);
        Assert.False((await GetAsync(db, otherProject, 2)).Deleted);
    }

    [Fact]
    public async Task An_empty_complete_listing_marks_every_row()
    {
        var (db, projectId) = await SeedAsync();
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(1), Row(2));

        Assert.Equal(2, await MarkAsync(db, projectId, [], T0.AddHours(1)));
        Assert.Equal(0, await MarkAsync(db, projectId, [], T0.AddHours(2)));
    }

    [Fact]
    public async Task Versions_are_recorded_once_with_the_rows_and_roll_back_with_them()
    {
        var (db, projectId) = await SeedAsync();
        await using (var context = Context(db))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new SubmissionStore(context).UpsertAsync(projectId, [Row(1)], SubmissionOrigin.Sync, T0, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await TestData.ScalarAsync<long>(db, "SELECT count(*) FROM tamiza.form_versions"));

        var withoutVersion = new SubmissionRow(3, "u-3", null, T0, "{}");
        var other = new SubmissionRow(4, "u-4", "v0", T0, "{}");
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0, Row(1), Row(2), withoutVersion, other);
        await UpsertAsync(db, projectId, SubmissionOrigin.Sync, T0.AddHours(1), Row(5));

        Assert.Equal("v0 pending,v1 pending", await TestData.ScalarAsync<string>(db,
            "SELECT string_agg(format('%s %s', uid, status), ',' ORDER BY uid) FROM tamiza.form_versions"));
    }
}
