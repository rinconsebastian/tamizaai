using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Startup_applies_migrations_to_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        factory.CreateClient().Dispose();

        Assert.True(await ScalarAsync<bool>(connectionString, "SELECT to_regclass('tamiza.users') IS NOT NULL"));
    }

    [Fact]
    public async Task Project_tables_constraints_and_indexes_exist()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new TamizaApiFactory(connectionString);
        factory.CreateClient().Dispose();

        foreach (var table in new[] { "projects", "project_members", "project_invitations", "sampling_frames" })
        {
            Assert.True(await ScalarAsync<bool>(connectionString, $"SELECT to_regclass('tamiza.{table}') IS NOT NULL"), table);
        }

        var roleCheck = await ScalarAsync<string>(connectionString,
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_project_members_role'");
        Assert.Contains("'admin'", roleCheck);
        Assert.Contains("'viewer'", roleCheck);

        var invitationIndex = await ScalarAsync<string>(connectionString,
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'tamiza' AND tablename = 'project_invitations' AND indexdef LIKE 'CREATE UNIQUE%' AND indexdef LIKE '%normalized_email%'");
        Assert.Contains("(project_id, normalized_email)", invitationIndex);

        Assert.Equal("boolean", await ScalarAsync<string>(connectionString,
            "SELECT data_type FROM information_schema.columns WHERE table_schema = 'tamiza' AND table_name = 'users' AND column_name = 'email_verified'"));
        Assert.Equal("jsonb", await ScalarAsync<string>(connectionString,
            "SELECT data_type FROM information_schema.columns WHERE table_schema = 'tamiza' AND table_name = 'sampling_frames' AND column_name = 'targets'"));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var badRole = new NpgsqlCommand(
            "INSERT INTO tamiza.project_members (project_id, user_id, role, created_at) VALUES (gen_random_uuid(), gen_random_uuid(), 'owner', now())",
            connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => badRole.ExecuteNonQueryAsync());
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.CheckViolation, PostgresErrorCodes.ForeignKeyViolation });
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
