using System.Globalization;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The step-up ticket, with the real HMAC (OI-150): it answers the demand of one
/// client's one request, for its lifetime, and nothing else.
/// </summary>
public class StepUpTicketServiceTests
{
    private const string ClientId = "CRM";
    private static readonly string FirstChallenge = new('1', 43);
    private static readonly string SecondChallenge = new('2', 43);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly RefreshTokenKeyService _keys = new(
        new Mock<IDataProtectionProvider>().Object,
        Options.Create(new JwtSettings
        {
            RefreshTokenHmacKeyPlain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));

    private readonly StepUpTicketService _service;
    private readonly DateTime _demandedAt = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

    public StepUpTicketServiceTests()
    {
        _service = new StepUpTicketService(_keys);
    }

    private bool Validate(string? ticket, string clientId, string? challenge, DateTime now, out DateTime demandedAt)
        => _service.TryValidate(ticket, clientId, challenge, now, Lifetime, out demandedAt);

    [Fact]
    public void TryValidate_TheRequestThatRaisedIt_ReturnsTheDemandTime()
    {
        var ticket = _service.Issue(ClientId, FirstChallenge, _demandedAt);

        Validate(ticket, ClientId, FirstChallenge, _demandedAt.AddMinutes(3), out var demandedAt).Should().BeTrue();
        demandedAt.Should().Be(_demandedAt);
    }

    [Fact]
    public void Issue_KeepsTheWireFormat_AndNeverCarriesTheChallenge()
    {
        var ticket = _service.Issue(ClientId, FirstChallenge, _demandedAt);

        var fields = ticket.Split('|');
        fields.Should().HaveCount(3);
        fields[0].Should().Be(new DateTimeOffset(_demandedAt).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        fields[1].Should().Be(ClientId);
        ticket.Should().NotContain(FirstChallenge);
    }

    /// <summary>Every way a ticket can fail to answer the request in hand.</summary>
    public static TheoryData<string> Refusals => new()
    {
        "another request of the same client",
        "another client",
        "past its lifetime",
        "from the future",
        "timestamp edited",
        "client edited",
        "signature edited",
        "old format, signed without the challenge",
        "no challenge in hand",
        "empty",
        "absent",
    };

    [Fact]
    public void Refusals_AreNotEmpty() => Refusals.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(Refusals))]
    public void TryValidate_AnythingButItsOwnRequestInItsLifetime_IsNoTicket(string refusal)
    {
        var ticket = _service.Issue(ClientId, FirstChallenge, _demandedAt);
        var unix = new DateTimeOffset(_demandedAt).ToUnixTimeSeconds();
        var signature = ticket[(ticket.LastIndexOf('|') + 1)..];
        var now = _demandedAt.AddMinutes(1);

        (string? Ticket, string ClientId, string? Challenge, DateTime At) attempt = refusal switch
        {
            "another request of the same client" => (ticket, ClientId, SecondChallenge, now),
            "another client" => (ticket, "OTHER", FirstChallenge, now),
            "past its lifetime" => (ticket, ClientId, FirstChallenge, _demandedAt + Lifetime + TimeSpan.FromSeconds(1)),
            "from the future" => (ticket, ClientId, FirstChallenge, _demandedAt.AddSeconds(-1)),
            "timestamp edited" => ($"{unix - 60}|{ClientId}|{signature}", ClientId, FirstChallenge, now),
            "client edited" => ($"{unix}|OTHER|{signature}", "OTHER", FirstChallenge, now),
            "signature edited" => ($"{unix}|{ClientId}|{(signature[0] == 'A' ? 'B' : 'A')}{signature[1..]}", ClientId, FirstChallenge, now),
            // What this server wrote before OI-150: the signature covers the first two
            // fields only. It fails, and counts as no ticket rather than an error.
            "old format, signed without the challenge" =>
                ($"{unix}|{ClientId}|{_keys.ComputeTokenHash($"{unix}|{ClientId}")}", ClientId, FirstChallenge, now),
            "no challenge in hand" => (ticket, ClientId, null, now),
            "empty" => (string.Empty, ClientId, FirstChallenge, now),
            "absent" => (null, ClientId, FirstChallenge, now),
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null)
        };

        var act = () => Validate(attempt.Ticket, attempt.ClientId, attempt.Challenge, attempt.At, out _);

        act.Should().NotThrow();
        Validate(attempt.Ticket, attempt.ClientId, attempt.Challenge, attempt.At, out var demandedAt).Should().BeFalse();
        demandedAt.Should().Be(default);
    }

    [Fact]
    public void TryValidate_AtTheEndOfItsLifetime_StillAnswers()
    {
        var ticket = _service.Issue(ClientId, FirstChallenge, _demandedAt);

        Validate(ticket, ClientId, FirstChallenge, _demandedAt + Lifetime, out _).Should().BeTrue();
    }

    [Fact]
    public void TryValidate_AClientIdHoldingTheSeparator_StillBindsTheChallenge()
    {
        // The signed text is unambiguous because the visible client id must equal
        // the request's: what follows it can only be that request's challenge.
        const string clientId = "A|B";
        var ticket = _service.Issue(clientId, FirstChallenge, _demandedAt);

        Validate(ticket, clientId, FirstChallenge, _demandedAt.AddMinutes(1), out _).Should().BeTrue();
        Validate(ticket, clientId, SecondChallenge, _demandedAt.AddMinutes(1), out _).Should().BeFalse();
        Validate(ticket, "A", "B|" + FirstChallenge, _demandedAt.AddMinutes(1), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Issue_WithoutAChallenge_Throws(string? challenge)
    {
        var act = () => _service.Issue(ClientId, challenge!, _demandedAt);

        act.Should().Throw<ArgumentException>("a ticket no request can present is a programming error");
    }

    [Fact]
    public void Issue_WithAChallengeHoldingTheSeparator_Throws()
    {
        var act = () => _service.Issue(ClientId, "x|" + FirstChallenge, _demandedAt);

        act.Should().Throw<ArgumentException>("the signed text must stay unambiguous whatever the caller validated");
    }

    [Fact]
    public void TryValidate_AChallengeHoldingTheSeparator_IsNoTicket()
    {
        // A ticket whose signature really covers that challenge, so nothing but
        // the separator rule can refuse it.
        var unix = new DateTimeOffset(_demandedAt).ToUnixTimeSeconds();
        var challenge = "x|" + FirstChallenge;
        var ticket = $"{unix}|{ClientId}|{_keys.ComputeTokenHash($"{unix}|{ClientId}|{challenge}")}";

        Validate(ticket, ClientId, challenge, _demandedAt.AddMinutes(1), out _).Should().BeFalse();
    }
}
