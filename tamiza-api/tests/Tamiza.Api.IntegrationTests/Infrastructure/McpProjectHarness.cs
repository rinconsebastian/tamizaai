using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Mcp;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A project with an admin, an analyst, a viewer and a superadmin who is not a member, plus an outsider, each connecting to the MCP
/// server as themselves. The project's access level and the deployment's maximum are given; the form is the standard survey.
/// </summary>
public sealed class McpProjectHarness : IAsyncDisposable
{
    public static readonly string[] People = ["viewer", "analyst", "admin", "superadmin", "outsider"];

    private readonly Dictionary<string, McpClient> _clients = [];

    private McpProjectHarness(McpHarness mcp, Guid projectId, Dictionary<string, Guid> users)
    {
        Mcp = mcp;
        ProjectId = projectId;
        Users = users;
    }

    public McpHarness Mcp { get; }

    public string Db => Mcp.ConnectionString;

    public TamizaApiFactory Factory => Mcp.Factory;

    public Guid ProjectId { get; }

    public IReadOnlyDictionary<string, Guid> Users { get; }

    public static async Task<McpProjectHarness> CreateAsync(
        PostgresFixture postgres, McpAccess level = McpAccess.Aggregates, McpAccess maximum = McpAccess.Rows,
        IReadOnlyDictionary<string, string?>? settings = null, string formFieldsJson = TestData.SurveyFieldsJson, string formChoicesJson = "[]",
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["TAMIZA_MCP_MAX_DATA_ACCESS"] = maximum == McpAccess.Rows ? "rows" : "aggregates",
            ["TAMIZA_SUPERADMIN_EMAILS"] = "superadmin@example.org",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            all[key] = value;
        }

        var mcp = await McpHarness.StartAsync(postgres, all, configureServices);
        var users = new Dictionary<string, Guid>();
        foreach (var person in People)
        {
            users[person] = await TestData.SignInAsync(mcp.Factory, person, $"{person}@example.org");
        }

        var project = await TestData.InsertProjectAsync(mcp.ConnectionString, users["admin"], "Water survey", formFieldsJson, formChoicesJson);
        await TestData.AddMemberAsync(mcp.ConnectionString, project, users["viewer"], "viewer");
        await TestData.AddMemberAsync(mcp.ConnectionString, project, users["analyst"], "analyst");
        await TestData.AddMemberAsync(mcp.ConnectionString, project, users["admin"], "admin");
        await TestData.ExecuteAsync(mcp.ConnectionString, "UPDATE tamiza.projects SET mcp_access = @level WHERE id = @id",
            ("level", level.ToString().ToLowerInvariant()), ("id", project));
        return new McpProjectHarness(mcp, project, users);
    }

    /// <summary>An MCP client connected as <paramref name="person"/>, kept for the life of the harness.</summary>
    public async Task<McpClient> ClientAsync(string person)
    {
        if (!_clients.TryGetValue(person, out var client))
        {
            _clients[person] = client = await Mcp.ConnectAsync(McpHarness.TokenFor(person, person, $"{person}@example.org"));
        }

        return client;
    }

    /// <summary>
    /// Calls a tool as <paramref name="person"/>. <c>projectId</c> is this project's unless the arguments name one. The arguments are an
    /// object whose properties are the tool's arguments.
    /// </summary>
    public async Task<CallToolResult> CallAsync(string person, string tool, object? arguments = null, bool withProject = true)
    {
        var values = new Dictionary<string, object?>();
        if (arguments is not null)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }

        if (withProject && !values.ContainsKey("projectId"))
        {
            values["projectId"] = ProjectId;
        }

        return await (await ClientAsync(person)).CallToolAsync(tool, values);
    }

    /// <summary>Changes the project's access level.</summary>
    public Task SetLevelAsync(McpAccess level) =>
        TestData.ExecuteAsync(Db, "UPDATE tamiza.projects SET mcp_access = @level WHERE id = @id", ("level", level.ToString().ToLowerInvariant()), ("id", ProjectId));

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        await Mcp.DisposeAsync();
    }
}
