using Microsoft.Extensions.Options;

namespace Tamiza.Api.Email;

/// <summary>
/// The operator's SMTP server, read from the flat <c>TAMIZA_SMTP_*</c> variables. Tamiza sends email only when the host and the sender
/// are both set (<see cref="Enabled"/>). Compose always passes optional strings, often empty, so a blank value means absent: the port
/// and the timeout are nullable for that reason. Text values are trimmed, so a password pasted with a trailing newline still works.
/// </summary>
public sealed class EmailOptions
{
    public const string HostVariable = "TAMIZA_SMTP_HOST";
    public const string PortVariable = "TAMIZA_SMTP_PORT";
    public const string SecurityVariable = "TAMIZA_SMTP_SECURITY";
    public const string UsernameVariable = "TAMIZA_SMTP_USERNAME";
    public const string PasswordVariable = "TAMIZA_SMTP_PASSWORD";
    public const string FromVariable = "TAMIZA_SMTP_FROM";
    public const string FromNameVariable = "TAMIZA_SMTP_FROM_NAME";
    public const string TimeoutVariable = "TAMIZA_SMTP_TIMEOUT_SECONDS";

    /// <summary>Upgrade a plain connection with STARTTLS, and fail when the server does not offer it.</summary>
    public const string StartTls = "starttls";

    /// <summary>TLS from the first byte (implicit TLS, usually port 465).</summary>
    public const string Tls = "tls";

    /// <summary>No encryption: only for a relay inside the operator's network or a test server.</summary>
    public const string None = "none";

    public const string DefaultFromName = "Tamiza";

    public const int DefaultTimeoutSeconds = 30;

    private string _host = "";
    private string _security = "";
    private string _username = "";
    private string _password = "";
    private string _from = "";
    private string _fromName = "";

    [ConfigurationKeyName(HostVariable)]
    public string Host { get => _host; set => _host = value?.Trim() ?? ""; }

    /// <summary>Null takes the port of the connection security (<see cref="EffectivePort"/>).</summary>
    [ConfigurationKeyName(PortVariable)]
    public int? Port { get; set; }

    /// <summary>One of <see cref="StartTls"/>, <see cref="Tls"/> or <see cref="None"/>, any case; empty means <see cref="StartTls"/>.</summary>
    [ConfigurationKeyName(SecurityVariable)]
    public string Security { get => _security; set => _security = value?.Trim().ToLowerInvariant() ?? ""; }

    [ConfigurationKeyName(UsernameVariable)]
    public string Username { get => _username; set => _username = value?.Trim() ?? ""; }

    /// <summary>Sent only to the SMTP server. Never logged, never returned.</summary>
    [ConfigurationKeyName(PasswordVariable)]
    public string Password { get => _password; set => _password = value?.Trim() ?? ""; }

    /// <summary>The sender address. It is never a recipient, so it needs no mailbox.</summary>
    [ConfigurationKeyName(FromVariable)]
    public string From { get => _from; set => _from = value?.Trim() ?? ""; }

    [ConfigurationKeyName(FromNameVariable)]
    public string FromName { get => _fromName; set => _fromName = value?.Trim() ?? ""; }

    [ConfigurationKeyName(TimeoutVariable)]
    public int? TimeoutSeconds { get; set; }

    public bool Enabled => Host.Length > 0 && From.Length > 0;

    public string SecurityMode => Security.Length == 0 ? StartTls : Security;

    public int EffectivePort => Port ?? SecurityMode switch
    {
        Tls => 465,
        None => 25,
        _ => 587,
    };

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds ?? DefaultTimeoutSeconds);

    public string SenderName => FromName.Length == 0 ? DefaultFromName : FromName;

    /// <summary>The validator makes sure the user name and the password are set together.</summary>
    public bool HasCredentials => Username.Length > 0 && Password.Length > 0;

    /// <summary>The one of the two variables that turn email on that is not set, when only the other is.</summary>
    public string? MissingVariable() =>
        (Host.Length > 0, From.Length > 0) switch
        {
            (true, false) => FromVariable,
            (false, true) => HostVariable,
            _ => null,
        };
}

/// <summary>Stops the api at startup, naming the variable, for values that are typos the operator must see at deploy time.</summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"{EmailOptions.PortVariable} must be between 1 and 65535, not {options.Port}.");
        }

        if (options.Security is not ("" or EmailOptions.StartTls or EmailOptions.Tls or EmailOptions.None))
        {
            failures.Add($"{EmailOptions.SecurityVariable} must be {EmailOptions.StartTls}, {EmailOptions.Tls} or {EmailOptions.None}, not '{options.Security}'.");
        }

        if (options.From.Length > 0 && !EmailAddressRule.IsValid(options.From))
        {
            failures.Add($"{EmailOptions.FromVariable} must be an email address such as reports@example.org, not '{options.From}'.");
        }

        if (options.TimeoutSeconds is < 5 or > 300)
        {
            failures.Add($"{EmailOptions.TimeoutVariable} must be between 5 and 300, not {options.TimeoutSeconds}.");
        }

        if ((options.Username.Length > 0) != (options.Password.Length > 0))
        {
            failures.Add($"{EmailOptions.UsernameVariable} and {EmailOptions.PasswordVariable} must be set together.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
