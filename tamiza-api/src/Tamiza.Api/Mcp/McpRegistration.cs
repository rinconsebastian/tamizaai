using System.Reflection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Tamiza.Api.CleanData;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Mcp;

public static class McpRegistration
{
    private static readonly string Version =
        typeof(McpRegistration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Registers the MCP server's options, authentication and tools. Everything is registered whether or not the server is enabled and is
    /// inert until <see cref="MapTamizaMcp"/> maps the endpoint, which only happens when <see cref="McpOptions.Enabled"/>: the
    /// settings are read when the application starts, not while its services are registered.
    /// </summary>
    public static IServiceCollection AddTamizaMcp(this IServiceCollection services)
    {
        // Flat TAMIZA_MCP_* variables bind from the configuration root, the tuning limits from the Mcp section.
        services.AddOptions<McpOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.Bind(options);
                configuration.GetSection(McpOptions.Section).Bind(options);
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<McpOptions>, McpOptionsValidator>();
        services.AddHostedService<McpStartupCheck>();

        // Stateless: each request is answered by itself and carries its token, so there is no session store and no affinity.
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "tamiza", Title = "Tamiza", Version = Version };
                options.ServerInstructions = McpGuide.Instructions;
            })
            .WithHttpTransport(transport => transport.SessionMode = HttpServerSessionMode.Stateless)
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpCallFilter.Create))
            .WithTools<McpProjectTools>()
            .WithTools<McpFormTools>()
            .WithTools<McpDataTools>()
            .WithTools<McpScriptTools>()
            .WithTools<McpPipelineTools>()
            .WithTools<McpDashboardTools>()
            .WithPrompts<McpPrompts>();

        services.AddMcpTools();
        services.AddSingleton<McpLimiter>();
        services.AddSingleton<McpLedger>();
        services.AddScoped<McpCallScope>();
        services.AddScoped<McpAccessGate>();
        services.AddSingleton<McpCallSweeper>();
        services.AddHostedService(provider => provider.GetRequiredService<McpCallSweeper>());
        return services;
    }

    private static IServiceCollection AddMcpTools(this IServiceCollection services)
    {
        services.AddScoped<McpTableReader>();
        services.AddScoped<McpMaskerFactory>();
        services.AddSingleton<ColumnProfiler>();
        return services;
    }

    /// <summary>
    /// Maps <c>/mcp</c> and the protected resource metadata when the server is enabled; otherwise both answer 404 to anyone.
    /// </summary>
    public static WebApplication MapTamizaMcp(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<IOptions<McpOptions>>().Value.Enabled)
        {
            // Without these, the fallback authorization policy would answer an anonymous request for a path that does not exist with 401.
            IResult NotFound() => TypedResults.NotFound();
            app.Map(McpOptions.Path, NotFound).AllowAnonymous().ExcludeFromDescription();
            app.Map("/.well-known/oauth-protected-resource/{**path}", NotFound).AllowAnonymous().ExcludeFromDescription();
            app.Map("/.well-known/oauth-protected-resource", NotFound).AllowAnonymous().ExcludeFromDescription();
            return app;
        }

        app.MapMcp(McpOptions.Path).RequireAuthorization();

        // RFC 9728: the document that tells a client which realm signs people in for this resource. Public: it holds nothing secret.
        // Both the path form for /mcp and the root form are served, because clients probe them in that order.
        app.MapGet(McpOptions.ResourceMetadataPath, ResourceMetadata).AllowAnonymous().WithName("GetMcpResourceMetadata");
        app.MapGet("/.well-known/oauth-protected-resource", ResourceMetadata).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static IResult ResourceMetadata(IOptions<TamizaOptions> tamiza, IOptions<OidcOptions> oidc) =>
        TypedResults.Json(new Dictionary<string, object>
        {
            ["resource"] = McpAuthentication.ResourceUrl(tamiza.Value),
            ["authorization_servers"] = new[] { oidc.Value.Authority.TrimEnd('/') },
            ["scopes_supported"] = new[] { McpOptions.Scope },
            ["bearer_methods_supported"] = new[] { "header" },
            ["resource_name"] = "Tamiza",
        });
}

/// <summary>Says once at startup that the hosted Claude apps cannot reach an address that is not public HTTPS.</summary>
internal sealed class McpStartupCheck(IOptions<McpOptions> mcp, IOptions<TamizaOptions> tamiza, ILogger<McpStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (mcp.Value.Enabled && McpOptions.IsNotReachableByHostedApps(tamiza.Value.PublicUrl))
        {
            logger.LogWarning(
                "The MCP server is enabled but TAMIZA_PUBLIC_URL is not an https address. The hosted Claude apps (claude.ai, Claude Desktop and mobile) will not connect; Claude Code on a computer that reaches this address will.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
