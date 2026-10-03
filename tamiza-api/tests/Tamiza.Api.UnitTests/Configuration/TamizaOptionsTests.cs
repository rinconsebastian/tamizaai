using Microsoft.Extensions.Configuration;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.UnitTests.Configuration;

public sealed class TamizaOptionsTests
{
    private static TamizaOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new TamizaOptions();
        configuration.Bind(options);
        return options;
    }

    [Fact]
    public void Kobo_private_networks_are_off_when_the_variable_is_absent() =>
        Assert.False(Bind().KoboAllowPrivateNetworks);

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    public void Kobo_private_networks_bind_from_the_variable(string value, bool expected) =>
        Assert.Equal(expected, Bind(("TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS", value)).KoboAllowPrivateNetworks);
}
