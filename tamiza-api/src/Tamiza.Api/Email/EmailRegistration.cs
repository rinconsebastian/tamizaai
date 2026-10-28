using Microsoft.Extensions.Options;

namespace Tamiza.Api.Email;

public static class EmailRegistration
{
    public static IServiceCollection AddTamizaEmail(this IServiceCollection services)
    {
        // Flat TAMIZA_SMTP_* variables bind from the configuration root, like the other deployment settings.
        services.AddOptions<EmailOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<EmailSenderSource>();
        services.AddHostedService<EmailStartupCheck>();
        return services;
    }
}

/// <summary>The sender while the host and the sender address are set, and none otherwise: then no SMTP connection is ever opened.</summary>
public sealed class EmailSenderSource(IOptions<EmailOptions> options, IEmailSender sender)
{
    public IEmailSender? Sender => options.Value.Enabled ? sender : null;
}

/// <summary>Says once at startup which variable is missing when email is only partly configured.</summary>
internal sealed class EmailStartupCheck(IOptions<EmailOptions> options, ILogger<EmailStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.MissingVariable() is { } missing)
        {
            logger.LogWarning(
                "Email is off because {Missing} is not set. Set both {Host} and {From} to turn it on.",
                missing, EmailOptions.HostVariable, EmailOptions.FromVariable);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
