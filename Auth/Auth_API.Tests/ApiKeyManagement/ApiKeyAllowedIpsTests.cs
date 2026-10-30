using System.Net;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth.Sdk.Validation;

namespace Auth_API.Tests.ApiKeyManagement;

/// <summary>
/// An API key's address list (OI-128 PR B, D-128-3 (b)): validated and normalized at creation by
/// <see cref="IpAllowList"/>, and applied by the SDK (<see cref="AllowedIpMatcher"/>) to the
/// connection's address. Ranges are compared as ranges, never as text.
/// </summary>
public class ApiKeyAllowedIpsTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData(" 203.0.113.0/24 ", "203.0.113.0/24")]
    [InlineData("2001:DB8:0:0::1", "2001:db8::1")]
    [InlineData("2001:db8::/32", "2001:db8::/32")]
    [InlineData("::ffff:198.51.100.7", "198.51.100.7")]
    [InlineData("::ffff:198.51.100.0/120", "198.51.100.0/24")]
    public void Create_NormalizesEachEntry(string entry, string normalized)
    {
        var list = IpAllowList.Create([entry]);

        list.IsError.Should().BeFalse();
        list.Value.Entries.Should().Equal(normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("10")]
    [InlineData("10.1")]
    [InlineData("010.000.000.000/8")]
    [InlineData("10.0.0.010")]
    [InlineData("0x0A.0.0.1")]
    [InlineData("10.0.0.1/24")]
    [InlineData("10.0.0.0/33")]
    [InlineData("2001:db8::1/64")]
    [InlineData("fe80::1%3")]
    [InlineData("203.0.113.7 , 203.0.113.8")]
    public void Create_RefusesAnythingButAnAddressOrAnExactRange(string entry)
    {
        IpAllowList.Create([entry]).FirstError.Code.Should().Be("ApiKey.AllowedIpInvalid");
        IpAllowList.IsValidEntry(entry).Should().BeFalse();
    }

    [Fact]
    public void Create_AcceptsFifty_RefusesFiftyOne_AndEmptyMeansAny()
    {
        IpAllowList.Create(Enumerable.Range(0, 50).Select(i => $"10.0.0.{i}")).IsError.Should().BeFalse();
        IpAllowList.Create(Enumerable.Range(0, 51).Select(i => $"10.0.0.{i}")).FirstError.Code.Should().Be("ApiKey.AllowedIpsTooMany");
        IpAllowList.Create(null).Value.Should().BeSameAs(IpAllowList.Any);
        IpAllowList.Create([]).Value.Entries.Should().BeEmpty();
        IpAllowList.Create(["10.0.0.1", "10.0.0.1"]).Value.Entries.Should().Equal("10.0.0.1");
    }

    [Fact]
    public void TheColumn_RoundTrips_AndAnUnreadableValueMatchesNothing()
    {
        var list = IpAllowList.Create(["203.0.113.0/24", "2001:db8::1"]).Value;

        var stored = ApiKeyRepository.SerializeAllowedIps(list);
        stored.Should().Be("[\"203.0.113.0/24\",\"2001:db8::1\"]");
        ApiKeyRepository.DeserializeAllowedIps(stored).Should().Be(list);
        ApiKeyRepository.SerializeAllowedIps(IpAllowList.Any).Should().BeNull("NULL means any address, as the column always meant");

        // Never widened to "any address" by a value this code did not write.
        var unreadable = ApiKeyRepository.DeserializeAllowedIps("not json");
        unreadable.Entries.Should().NotBeEmpty();
        AllowedIpMatcher.IsAllowed(unreadable.Entries, IPAddress.Parse("203.0.113.7")).Should().BeFalse();
    }

    public static TheoryData<string[], string, bool> Matches => new()
    {
        { ["203.0.113.0/24"], "203.0.113.77", true },
        { ["203.0.113.0/24"], "203.0.114.1", false },
        { ["203.0.113.7"], "203.0.113.7", true },
        { ["203.0.113.7"], "203.0.113.8", false },
        { ["2001:db8::/32"], "2001:db8:1234::42", true },
        { ["2001:db8::/32"], "2001:db9::1", false },
        { ["2001:db8::1"], "2001:0db8:0000:0000:0000:0000:0000:0001", true },
        // An IPv4 caller seen on a dual-stack socket is unmapped first.
        { ["198.51.100.0/24"], "::ffff:198.51.100.9", true },
        { ["198.51.100.7"], "::ffff:198.51.100.7", true },
        // A family mismatch never matches.
        { ["0.0.0.0/0"], "2001:db8::1", false },
        { ["10.0.0.0/8", "2001:db8::/32"], "10.20.30.40", true },
    };

    [Fact]
    public void Matches_IsNotEmpty() => Matches.Count<object[]>().Should().Be(11);

    [Theory]
    [MemberData(nameof(Matches))]
    public void TheSdk_ComparesRangesAndAddresses_NotText(string[] allowed, string remote, bool expected)
    {
        AllowedIpMatcher.IsAllowed(allowed, IPAddress.Parse(remote)).Should().Be(expected);
    }

    [Fact]
    public void AnEmptyList_AllowsAnyAddress_AndAnUnknownAddressMatchesNoList()
    {
        AllowedIpMatcher.IsAllowed([], IPAddress.Parse("203.0.113.7")).Should().BeTrue();
        AllowedIpMatcher.IsAllowed([], null).Should().BeTrue();
        AllowedIpMatcher.IsAllowed(["203.0.113.0/24"], null).Should().BeFalse();
    }
}
