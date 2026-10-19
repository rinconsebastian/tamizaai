using Microsoft.Extensions.Options;

namespace Tamiza.Api.Accounts;

/// <summary>Settings of account emails that do not depend on the identity provider, read from <c>TAMIZA_ACCOUNT_EMAILS_*</c>.</summary>
public sealed class AccountEmailOptions
{
    public const string PerHourVariable = "TAMIZA_ACCOUNT_EMAILS_PER_HOUR";
    public const int DefaultPerHour = 20;

    /// <summary>How many account emails one user may cause per hour: invitations, resends and password resets together.</summary>
    [ConfigurationKeyName(PerHourVariable)]
    public int PerHour { get; set; } = DefaultPerHour;
}

public sealed class AccountEmailOptionsValidator : IValidateOptions<AccountEmailOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountEmailOptions options) =>
        options.PerHour is < 1 or > 1000
            ? ValidateOptionsResult.Fail($"{AccountEmailOptions.PerHourVariable} must be between 1 and 1000, not {options.PerHour}.")
            : ValidateOptionsResult.Success;
}
