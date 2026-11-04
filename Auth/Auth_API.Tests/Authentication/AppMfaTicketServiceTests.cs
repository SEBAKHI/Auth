using Auth.Domain.Enums;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S23, AM8 (8-1): the application two-step ticket, with the real keyring. It answers one
/// client's one request, for one person and one reason, for 15 minutes, and nothing else.
/// It is signed under its own keyring purpose, never the step-up ticket's.
/// </summary>
/// <remarks>
/// Deliberate break: drop the user id from the signed text (keep it visible); the test that
/// reads a ticket edited to another user turns red.
/// </remarks>
public class AppMfaTicketServiceTests
{
    private const string ClientId = "CRM";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly string FirstChallenge = new('1', 43);
    private static readonly string SecondChallenge = new('2', 43);

    private readonly KeyringCredentialHasher _keys = TestCredentialHashers.SingleKey();
    private readonly AppMfaTicketService _service;
    private readonly DateTime _issuedAt = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    public AppMfaTicketServiceTests()
    {
        _service = new AppMfaTicketService(_keys);
    }

    private string Issue(ApplicationMfaRequirement reason = ApplicationMfaRequirement.StepUp, string clientId = ClientId) =>
        _service.Issue(clientId, UserId, reason, FirstChallenge, _issuedAt);

    private bool Read(string? ticket, out ApplicationMfaRequirement reason,
        string clientId = ClientId, Guid? userId = null, string? challenge = null, int minutesLater = 3) =>
        _service.TryRead(ticket, clientId, userId ?? UserId, challenge ?? FirstChallenge, _issuedAt.AddMinutes(minutesLater), out reason);

    [Theory]
    [InlineData(ApplicationMfaRequirement.StepUp)]
    [InlineData(ApplicationMfaRequirement.Passkey)]
    [InlineData(ApplicationMfaRequirement.Enroll)]
    [InlineData(ApplicationMfaRequirement.Reauthenticate)]
    public void TryRead_TheRequestThatRaisedIt_ReturnsItsReason(ApplicationMfaRequirement reason)
    {
        Read(Issue(reason), out var read).Should().BeTrue();
        read.Should().Be(reason);
    }

    [Fact]
    public void Issue_CarriesTheChallengeFingerprint_NeverTheChallenge()
    {
        var ticket = Issue();

        var fields = ticket.Split('|');
        fields.Should().HaveCount(6);
        fields[1].Should().Be(ClientId);
        fields[2].Should().Be(UserId.ToString("N"));
        fields[3].Should().Be(((int)ApplicationMfaRequirement.StepUp).ToString());
        ticket.Should().NotContain(FirstChallenge);
    }

    [Fact]
    public void TryRead_AnotherUser_IsFalse()
    {
        Read(Issue(), out _, userId: Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void TryRead_ATicketEditedToAnotherUser_IsFalse()
    {
        // The user is inside the signature: rewriting the visible field breaks it.
        var fields = Issue().Split('|');
        var otherUser = Guid.NewGuid();
        fields[2] = otherUser.ToString("N");

        Read(string.Join('|', fields), out _, userId: otherUser).Should().BeFalse();
    }

    [Fact]
    public void TryRead_ATicketEditedToAnotherReason_IsFalse()
    {
        var fields = Issue(ApplicationMfaRequirement.Enroll).Split('|');
        fields[3] = ((int)ApplicationMfaRequirement.StepUp).ToString();

        Read(string.Join('|', fields), out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_AnotherRequestOfTheSameClient_IsFalse()
    {
        // A new authorize of the application is a new redirect, never the loop guard's refusal.
        Read(Issue(), out _, challenge: SecondChallenge).Should().BeFalse();
    }

    [Fact]
    public void TryRead_AnotherClient_IsFalse()
    {
        Read(Issue(), out _, clientId: "SHOP").Should().BeFalse();
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(-1, false)]
    public void TryRead_LivesFifteenMinutes_AndNeverFromTheFuture(int minutesLater, bool expected)
    {
        Read(Issue(), out _, minutesLater: minutesLater).Should().Be(expected);
        AppMfaTicketService.Lifetime.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("1|CRM|x|1|y|")]
    [InlineData("||||||")]
    public void TryRead_AnythingMalformed_IsFalse(string? ticket)
    {
        Read(ticket, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_InTheSameSecondItWasIssued_Reads()
    {
        // A bounce straight back must meet the guard: an issue time rounded up would read as
        // from the future, and the fastest loop would get past it.
        var issuedAt = _issuedAt.AddMilliseconds(400);
        var ticket = _service.Issue(ClientId, UserId, ApplicationMfaRequirement.Enroll, FirstChallenge, issuedAt);

        _service.TryRead(ticket, ClientId, UserId, FirstChallenge, issuedAt.AddMilliseconds(100), out var reason)
            .Should().BeTrue();
        reason.Should().Be(ApplicationMfaRequirement.Enroll);
    }

    [Fact]
    public void TryRead_AClientIdHoldingTheSeparator_StillReads()
    {
        var ticket = Issue(clientId: "A|B");

        Read(ticket, out var reason, clientId: "A|B").Should().BeTrue();
        reason.Should().Be(ApplicationMfaRequirement.StepUp);
    }

    [Fact]
    public void TryRead_AStepUpTicket_IsNeverAnAppMfaTicket()
    {
        // Two purposes, two subkeys: a step-up ticket's signature does not verify here.
        var stepUp = new StepUpTicketService(_keys).Issue(ClientId, FirstChallenge, _issuedAt);

        Read(stepUp, out _).Should().BeFalse();
    }

    [Fact]
    public void BelongsTo_TheRaisingRequest_WhoeverTheUser()
    {
        var ticket = Issue();

        _service.BelongsTo(ticket, ClientId, FirstChallenge, _issuedAt.AddMinutes(1)).Should().BeTrue();
        _service.BelongsTo(ticket, ClientId, SecondChallenge, _issuedAt.AddMinutes(1)).Should().BeFalse();
        _service.BelongsTo(ticket, "SHOP", FirstChallenge, _issuedAt.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Issue_NoneIsNoReason_Throws()
    {
        var act = () => _service.Issue(ClientId, UserId, ApplicationMfaRequirement.None, FirstChallenge, _issuedAt);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
