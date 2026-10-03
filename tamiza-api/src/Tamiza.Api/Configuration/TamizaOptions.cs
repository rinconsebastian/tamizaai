using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Configuration;

/// <summary>Deployment settings read from the flat <c>TAMIZA_*</c> environment variables.</summary>
public sealed class TamizaOptions
{
    public const string DefaultDataProtectionKeysPath = "/var/lib/tamiza/keys";

    /// <summary>Public base URL of the installation, as users and Kobo reach it.</summary>
    [Required, Url]
    [ConfigurationKeyName("TAMIZA_PUBLIC_URL")]
    public string PublicUrl { get; set; } = "";

    /// <summary>Comma-separated emails granted the global superadmin role.</summary>
    [ConfigurationKeyName("TAMIZA_SUPERADMIN_EMAILS")]
    public string SuperAdminEmails { get; set; } = "";

    [ConfigurationKeyName("TAMIZA_MIGRATE_ON_STARTUP")]
    public bool MigrateOnStartup { get; set; } = true;

    [Required]
    [ConfigurationKeyName("TAMIZA_DATA_PROTECTION_KEYS_PATH")]
    public string DataProtectionKeysPath { get; set; } = DefaultDataProtectionKeysPath;
}
