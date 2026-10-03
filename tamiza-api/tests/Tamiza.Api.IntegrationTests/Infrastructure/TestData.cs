using System.Net.Http.Json;
using Npgsql;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>Seeds users, projects and memberships directly, for tests that are not about creating them.</summary>
public static class TestData
{
    private sealed record Me(Guid Id);

    /// <summary>Signs a user in through the API (which provisions them) and returns their local id.</summary>
    public static async Task<Guid> SignInAsync(TamizaApiFactory factory, string subject, string email, bool emailVerified = true)
    {
        using var client = factory.CreateClient().WithToken(TestTokens.Create(subject, name: subject, email: email, emailVerified: emailVerified));
        var me = await client.GetFromJsonAsync<Me>("/api/v1/me");
        return me!.Id;
    }

    public static HttpClient ClientFor(TamizaApiFactory factory, string subject, string email, bool emailVerified = true) =>
        factory.CreateClient().WithToken(TestTokens.Create(subject, name: subject, email: email, emailVerified: emailVerified));

    public const string SurveyFieldsJson = """
        [{"name":"municipality","xpath":"municipality","type":"select_one","label":"Municipality"},
         {"name":"sex","xpath":"sex","type":"select_one","label":"Sex"},
         {"name":"age_range","xpath":"group/age_range","type":"select_one","label":"Age range"}]
        """;

    public static async Task<Guid> InsertProjectAsync(string connectionString, Guid createdBy, string name = "Seeded project", string formFieldsJson = "[]")
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(connectionString,
            """
            INSERT INTO tamiza.projects (id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret,
                                         form_name, form_fields, form_checked_at, created_by, created_at, updated_at)
            VALUES (@id, @name, 'https://kf.kobotoolbox.org', 'aSeededAsset', 'x', 'x', 'Seeded form', @fields::jsonb, now(), @createdBy, now(), now())
            """,
            ("id", id), ("name", name), ("createdBy", createdBy), ("fields", formFieldsJson));
        return id;
    }

    public static Task AddMemberAsync(string connectionString, Guid projectId, Guid userId, string role) =>
        ExecuteAsync(connectionString,
            "INSERT INTO tamiza.project_members (project_id, user_id, role, created_at) VALUES (@projectId, @userId, @role, now())",
            ("projectId", projectId), ("userId", userId), ("role", role));

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
