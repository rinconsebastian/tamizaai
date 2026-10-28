using Tamiza.Api.Email;

namespace Tamiza.Api.UnitTests.Email;

public sealed class EmailAddressRuleTests
{
    [Theory]
    [InlineData("ana@partner.org")]
    [InlineData("luis@city.gov.co")]
    [InlineData(" maria@ngo.org ")]
    [InlineData("first.last+reports@sub.example.org")]
    public void Bare_addresses_of_any_domain_are_valid(string address) => Assert.True(EmailAddressRule.IsValid(address));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("luis")]
    [InlineData("luis at city")]
    [InlineData("Ana <ana@partner.org>")]
    [InlineData("ana@partner.org (Ana)")]
    [InlineData("ana@partner.org, luis@city.gov.co")]
    [InlineData("@partner.org")]
    [InlineData("ana.@partner.org")]
    [InlineData("ana@partner.org.")]
    [InlineData(null)]
    public void Anything_but_a_bare_address_is_invalid(string? address) => Assert.False(EmailAddressRule.IsValid(address));

    [Fact]
    public void An_address_longer_than_an_smtp_path_is_invalid()
    {
        var local = new string('a', 64);
        var domain = string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".org";
        var address = $"{local}@{domain}";

        Assert.True(address.Length > EmailAddressRule.MaxLength);
        Assert.False(EmailAddressRule.IsValid(address));
    }
}
