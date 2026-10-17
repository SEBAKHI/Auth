using Auth.Application.Features.Authentication.ClassifyTokenClient;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Which application a token request counts as (OI-178): the grant decides the key purpose, the
/// authenticator verifies, and a spent jti is not counted as its application.
/// </summary>
public class ClassifyTokenClientQueryHandlerTests
{
    private static readonly Guid Application = Guid.NewGuid();
    private readonly Mock<IClientAuthenticator> _authenticator = new();
    private readonly Mock<IClientAssertionJtiStore> _jtis = new();

    private Task<ErrorOr<Guid>> Classify(string? grantType) =>
        new ClassifyTokenClientQueryHandler(_authenticator.Object, _jtis.Object).Handle(
            new ClassifyTokenClientQuery(grantType, "type", "assertion", "shop", "203.0.113.7"), CancellationToken.None);

    private void Verifies(MachineKeyPurpose purpose) =>
        _authenticator.Setup(a => a.VerifyAsync(It.IsAny<ClientAssertionRequest>(), purpose, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticatedClient(Guid.NewGuid(), "kid", Application, "shop", "jti-1", DateTime.UtcNow.AddMinutes(1)));

    [Theory]
    [InlineData("authorization_code", MachineKeyPurpose.SignIn)]
    [InlineData("refresh_token", MachineKeyPurpose.SignIn)]
    [InlineData("client_credentials", MachineKeyPurpose.Machine)]
    public async Task AVerifiedAssertion_CountsAsItsApplication_WithThePurposeItsGrantRequires(string grant, MachineKeyPurpose purpose)
    {
        Verifies(purpose);

        var classified = await Classify(grant);

        classified.Value.Should().Be(Application);
        _authenticator.Verify(a => a.VerifyAsync(
            It.Is<ClientAssertionRequest>(r => r.AssertionType == "type" && r.Assertion == "assertion" && r.ClientId == "shop"),
            purpose, It.IsAny<CancellationToken>()), Times.Once);
        _jtis.Verify(j => j.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never, "classifying spends nothing");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("password")]
    [InlineData("REFRESH_TOKEN")]
    public async Task AGrantThatTakesNoAssertion_IsNoApplication(string? grant)
    {
        Verifies(MachineKeyPurpose.SignIn);

        (await Classify(grant)).FirstError.Should().Be(AuthErrors.InvalidClient);
        _authenticator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnAssertionThatDoesNotVerify_IsNoApplication()
    {
        _authenticator.Setup(a => a.VerifyAsync(It.IsAny<ClientAssertionRequest>(), It.IsAny<MachineKeyPurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.InvalidClient);

        (await Classify("refresh_token")).FirstError.Should().Be(AuthErrors.InvalidClient);
    }

    [Fact]
    public async Task ASpentAssertion_IsNoApplication()
    {
        // Whoever captured one valid assertion could replay it for its lifetime: refused by the
        // grant every time, it must not be counted on the application's budget first.
        Verifies(MachineKeyPurpose.SignIn);
        _jtis.Setup(j => j.IsSpentAsync(Application, "jti-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await Classify("refresh_token")).FirstError.Should().Be(AuthErrors.InvalidClient);
    }
}
