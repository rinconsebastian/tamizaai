using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Configuration;
using Tamiza.Api.Security;

namespace Tamiza.Api.UnitTests.Security;

public sealed class DataProtectionTests : IDisposable
{
    private readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("tamiza-keys-");

    [Fact]
    public void A_second_instance_with_the_same_key_directory_unprotects_the_payload()
    {
        const string secret = "kobo-token-value";

        var protectedValue = CreateProtector().Protect(secret);

        Assert.NotEqual(secret, protectedValue);
        Assert.Equal(secret, CreateProtector().Unprotect(protectedValue));
        Assert.NotEmpty(_keys.GetFiles("key-*.xml"));
    }

    private IDataProtector CreateProtector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<TamizaOptions>().Configure(options => options.DataProtectionKeysPath = _keys.FullName);
        services.AddTamizaDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
    }

    public void Dispose() => _keys.Delete(recursive: true);
}
