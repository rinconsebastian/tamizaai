using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Performance;

namespace Tamiza.Api.IntegrationTests.Performance;

/// <summary>Counts computations and, when asked, holds each one open after it has taken its slot.</summary>
public sealed class TestProbe : IPerformanceProbe
{
    private int _started;
    private volatile TaskCompletionSource _gate = Open();

    public int Started => Volatile.Read(ref _started);

    /// <summary>Computations that start from now on wait until <see cref="Release"/>.</summary>
    public void Hold() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _gate.TrySetResult();

    public async ValueTask ComputationStartedAsync(Guid projectId)
    {
        Interlocked.Increment(ref _started);
        await _gate.Task;
    }

    public async Task WaitForStartedAsync(int count, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (Started < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Only {Started} of {count} computations started.");
            }

            await Task.Delay(20);
        }
    }

    private static TaskCompletionSource Open()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }
}

/// <summary>
/// A project whose submissions are rows written straight into <c>raw_submissions</c>, with a viewer, an analyst and an admin,
/// and a form like a household survey's: <c>start</c>, <c>end</c>, <c>username</c> and a few questions.
/// </summary>
public sealed class PerformanceHarness : IAsyncDisposable
{
    public const string Fields = """
        [{"name":"start","xpath":"start","type":"start"},
         {"name":"end","xpath":"end","type":"end"},
         {"name":"username","xpath":"username","type":"username"},
         {"name":"enumerator_code","xpath":"enumerator_code","type":"text"},
         {"name":"municipality","xpath":"municipality","type":"select_one","listName":"municipalities"},
         {"name":"sex","xpath":"sex","type":"select_one","listName":"sexes"},
         {"name":"age","xpath":"group/age","type":"select_one","listName":"ages"},
         {"name":"languages","xpath":"languages","type":"select_multiple","listName":"languages"},
         {"name":"member_age","xpath":"members/member_age","type":"select_one","listName":"ages","inRepeat":true}]
        """;

    public const string Choices = """
        [{"listName":"municipalities","name":"cali","label":"Cali municipality"},
         {"listName":"municipalities","name":"bogota","label":"Bogotá"},
         {"listName":"sexes","name":"f","label":"Female"},
         {"listName":"sexes","name":"m","label":"Male"},
         {"listName":"ages","name":"18_29","label":"18 to 29"},
         {"listName":"ages","name":"30_44","label":"30 to 44"}]
        """;

    private long _koboId;

    private PerformanceHarness(TamizaApiFactory factory, string db, Guid projectId, Guid adminId, HttpClient viewer, HttpClient analyst, HttpClient admin)
    {
        Factory = factory;
        Db = db;
        ProjectId = projectId;
        AdminId = adminId;
        Viewer = viewer;
        Analyst = analyst;
        Admin = admin;
    }

    public TamizaApiFactory Factory { get; }

    public string Db { get; }

    public Guid ProjectId { get; }

    public Guid AdminId { get; }

    public HttpClient Viewer { get; }

    public HttpClient Analyst { get; }

    public HttpClient Admin { get; }

    public string Url => $"/api/v1/projects/{ProjectId}/performance";

    public string FrameUrl => $"/api/v1/projects/{ProjectId}/sampling-frame";

    public static async Task<PerformanceHarness> CreateAsync(
        PostgresFixture postgres,
        string fieldsJson = Fields,
        string choicesJson = Choices,
        string? timeZone = null,
        string? enumeratorField = null,
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        // Idle connections of a finished test are pruned after a few seconds: the whole suite shares one server with a connection limit.
        var db = await postgres.CreateDatabaseAsync() + ";Connection Idle Lifetime=3;Connection Pruning Interval=1";
        var factory = new TamizaApiFactory(db, settings, configureServices);
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adminId, formFieldsJson: fieldsJson);
        await TestData.ExecuteAsync(db,
            "UPDATE tamiza.projects SET form_choices = @choices::jsonb, time_zone = coalesce(@zone, 'UTC'), enumerator_field = @enumerator",
            ("choices", choicesJson), ("zone", (object?)timeZone ?? DBNull.Value), ("enumerator", (object?)enumeratorField ?? DBNull.Value));
        await TestData.AddMemberAsync(db, projectId, adminId, "admin");
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, projectId, analystId, "analyst");
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, projectId, viewerId, "viewer");
        return new PerformanceHarness(factory, db, projectId, adminId,
            TestData.ClientFor(factory, "vic", "vic@example.org"),
            TestData.ClientFor(factory, "grace", "grace@example.org"),
            TestData.ClientFor(factory, "ada", "ada@example.org"));
    }

    /// <summary>The object as a submission's answers: keys are the full paths of the questions.</summary>
    public static string Doc(params (string Key, string? Value)[] answers) =>
        JsonSerializer.Serialize(answers.ToDictionary(a => a.Key, a => a.Value));

    /// <summary>Stores one submission exactly as given; <paramref name="submittedAt"/> null means Kobo gave no reception time.</summary>
    public async Task AddAsync(string dataJson, string? submittedAt = "2026-09-01T10:00:00Z", bool deleted = false)
    {
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, submitted_at, data, origin, received_at, updated_at, deleted, deleted_at)
            VALUES (@project, @id, @uuid, @at, @data, 'sync', now(), now(), @deleted, CASE WHEN @deleted THEN now() END)
            """, connection);
        command.Parameters.AddWithValue("project", ProjectId);
        var id = Interlocked.Increment(ref _koboId);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("uuid", $"uuid-{id}");
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz)
        {
            Value = submittedAt is null ? DBNull.Value : DateTimeOffset.Parse(submittedAt, System.Globalization.CultureInfo.InvariantCulture),
        });
        command.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = dataJson });
        command.Parameters.AddWithValue("deleted", deleted);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Stores <paramref name="count"/> identical submissions in one statement.</summary>
    public async Task AddManyAsync(int count, string dataJson, string? submittedAt = "2026-09-01T10:00:00Z", bool deleted = false)
    {
        var first = Interlocked.Add(ref _koboId, count) - count;
        await using var connection = new NpgsqlConnection(Db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, submitted_at, data, origin, received_at, updated_at, deleted, deleted_at)
            SELECT @project, @first + g, 'uuid-' || (@first + g), @at, @data, 'sync', now(), now(), @deleted, CASE WHEN @deleted THEN now() END
            FROM generate_series(1, @count) g
            """, connection);
        command.Parameters.AddWithValue("project", ProjectId);
        command.Parameters.AddWithValue("first", first);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz)
        {
            Value = submittedAt is null ? DBNull.Value : DateTimeOffset.Parse(submittedAt, System.Globalization.CultureInfo.InvariantCulture),
        });
        command.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = dataJson });
        command.Parameters.AddWithValue("deleted", deleted);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Stores a submission with the given answers and a completion time of <paramref name="minutes"/> minutes.</summary>
    public Task AddTimedAsync(int minutes, string? enumerator = null, string submittedAt = "2026-09-01T10:00:00Z", params (string Key, string? Value)[] more)
    {
        var start = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var answers = new List<(string, string?)>
        {
            ("start", start.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz")),
            ("end", start.AddMinutes(minutes).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz")),
            ("username", enumerator),
        };
        answers.AddRange(more.Select(m => (m.Key, m.Value)));
        return AddAsync(Doc(answers.ToArray()), submittedAt);
    }

    public async Task<HttpResponseMessage> RawGetAsync(string query = "", HttpClient? client = null, CancellationToken ct = default) =>
        await (client ?? Viewer).GetAsync(Url + query, ct);

    public async Task<JsonElement> GetAsync(string query = "", HttpClient? client = null)
    {
        var response = await RawGetAsync(query, client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<HttpResponseMessage> SaveFrameAsync(object frame) => await Analyst.PutAsJsonAsync(FrameUrl, frame);

    /// <summary>A frame with one row per given combination; the dimensions map to the form's questions.</summary>
    public static object Frame(string[] fields, params (string[] Values, int Target)[] rows) => new
    {
        dimensions = fields.Select(f => new { name = f, field = f }).ToArray(),
        targets = rows.Select(r => new { values = r.Values, target = r.Target }).ToArray(),
    };

    /// <summary>The suite shares one server with a connection limit, and a finished test database would keep its idle connections for minutes.</summary>
    public static void ReleasePool(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => TestData.ExecuteAsync(Db, sql, parameters);

    public async ValueTask DisposeAsync()
    {
        Viewer.Dispose();
        Analyst.Dispose();
        Admin.Dispose();
        await Factory.DisposeAsync();
        ReleasePool(Db);
    }
}
