using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Security;

public static class DataProtectionRegistration
{
    public const string ApplicationName = "tamiza";

    /// <summary>
    /// Persists the key ring on the file system (a volume only the API mounts), never in the database,
    /// so database access alone cannot decrypt stored secrets.
    /// </summary>
    public static IServiceCollection AddTamizaDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName(ApplicationName);
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<TamizaOptions>, ILoggerFactory>((keys, tamiza, loggerFactory) =>
                keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(tamiza.Value.DataProtectionKeysPath), loggerFactory));
        return services;
    }
}
