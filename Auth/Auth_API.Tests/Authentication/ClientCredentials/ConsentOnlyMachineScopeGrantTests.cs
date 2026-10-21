using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ClientCredentials;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// The consent-only machine scope at the grant (OI-202, T1, V2): never in a request that names no
/// scope, granted only when asked for AND consented, and refused before the jti is spent when
/// asked for without consent. The same in-process grant as
/// <see cref="ClientCredentialsCommandHandlerTests"/>: a real assertion, the real verifier, the
/// real token service; only the stores are mocks.
/// </summary>
public sealed class ConsentOnlyMachineScopeGrantTests : IDisposable
{
    private static readonly Guid ApplicationId = Guid.NewGuid();

    private readonly MachineCredentials _credentials = new();
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IClientAssertionJtiStore> _jtis = new();
    private readonly JwtSettings _settings = new() { Issuer = MachineCredentials.Issuer, Audience = "authsystem-api", KeyId = "test-key" };
    private readonly JwtTokenService _tokens;
    private readonly AppEntity _application = TestHelpers.CreateApplication(id: ApplicationId, code: MachineCredentials.ClientId);

    public ConsentOnlyMachineScopeGrantTests()
    {
        _tokens = new JwtTokenService(Options.Create(_settings), Mock.Of<IPasswordHasher>());
        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => kid == _credentials.Kid
                ? new MachineKeyCredential(
                    new ApplicationMachineKey(
                        Guid.NewGuid(), ApplicationId, _credentials.Kid, "server", MachineClients.SigningAlgorithm,
                        _credentials.PublicKey.PublicKeyJwk, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), null, null, null, null),
                    MachineCredentials.ClientId)
                : null);
        _jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _applications.Setup(r => r.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
    }

    public void Dispose()
    {
        _tokens.Dispose();
        _credentials.Dispose();
    }

    private Task<ErrorOr<ClientCredentialsTokenResponse>> SendAsync(string? scope) =>
        new ClientCredentialsCommandHandler(
                new ClientAuthenticator(
                    _keys.Object, _jtis.Object, new ClientAssertionVerifier(TimeProvider.System),
                    TestHelpers.CreateOptions(_settings), NullLogger<ClientAuthenticator>.Instance),
                _keys.Object,
                _applications.Object,
                _tokens,
                NullLogger<ClientCredentialsCommandHandler>.Instance)
            .Handle(
                new ClientCredentialsCommand(ClientCredentialsCommandHandler.JwtBearerAssertionType, _credentials.Assertion(), null, scope, "203.0.113.7"),
                CancellationToken.None);

    private void TheJtiWasNotSpent() =>
        _jtis.Verify(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnEmptyScope_GrantsExactlyTheThreeDefaultScopes_ConsentOrNot(bool consented)
    {
        _application.SetMachineOrganizationsRead(consented, Guid.NewGuid());

        var result = await SendAsync(scope: null);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : null);
        result.Value.Scope.Should().Be("apikeys.validate webhookkeys.validate tokens.introspect");
        _applications.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "a request for the default scopes costs no read");
    }

    [Fact]
    public void TheConsentOnlyScope_IsNotADefaultScope_NorAUserScope()
    {
        MachineClients.Scopes.All.Should().Equal("apikeys.validate", "webhookkeys.validate", "tokens.introspect");
        MachineClients.Scopes.ConsentOnly.Should().Equal("organizations.read.all");
        OAuthScopes.Supported.Should().NotContain(MachineClients.Scopes.OrganizationsReadAll, "the user grants never accept it");
        Auth.Domain.ValueObjects.ScopeSet.FromStored("openid organizations.read.all").Contains(MachineClients.Scopes.OrganizationsReadAll)
            .Should().BeFalse("a user grant's scope set drops it as unknown");
    }

    [Fact]
    public async Task AskedFor_WithoutConsent_IsInvalidScope_AndSpendsNothing()
    {
        _application.SetMachineOrganizationsRead(false, Guid.NewGuid()).Should().BeTrue();

        var result = await SendAsync("organizations.read.all");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidScope.Code);
        TheJtiWasNotSpent();
    }

    [Fact]
    public async Task AskedFor_ByAnApplicationThatIsGone_IsInvalidScope_AndSpendsNothing()
    {
        _applications.Setup(r => r.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync((AppEntity?)null);

        var result = await SendAsync("organizations.read.all");

        result.FirstError.Code.Should().Be(AuthErrors.InvalidScope.Code);
        TheJtiWasNotSpent();
    }

    [Fact]
    public async Task AskedFor_WithConsent_IsGranted_InTheCataloguesOrder()
    {
        _application.AllowMachineOrganizationsRead.Should().BeTrue("on by default (D-202-2)");

        var alone = await SendAsync("organizations.read.all");
        var withAnother = await SendAsync("organizations.read.all tokens.introspect");

        alone.IsError.Should().BeFalse(alone.IsError ? alone.FirstError.Code : null);
        alone.Value.Scope.Should().Be("organizations.read.all");
        withAnother.Value.Scope.Should().Be("tokens.introspect organizations.read.all", "default scopes first, then the consent-only ones");
    }

    [Theory]
    [InlineData("Organizations.Read.All")]
    [InlineData("organizations.read")]
    [InlineData("organizations.read.all.x")]
    public async Task ANearMiss_IsAnUnknownScope(string scope)
    {
        var result = await SendAsync(scope);

        result.FirstError.Code.Should().Be(AuthErrors.InvalidScope.Code);
        TheJtiWasNotSpent();
    }

    [Fact]
    public void GrantedScopes_IsThePureRule()
    {
        ClientCredentialsCommandHandler.GrantedScopes(null, MachineClients.Scopes.ConsentOnly).Should().Equal(MachineClients.Scopes.All);
        ClientCredentialsCommandHandler.GrantedScopes("  ", MachineClients.Scopes.ConsentOnly).Should().Equal(MachineClients.Scopes.All);
        ClientCredentialsCommandHandler.GrantedScopes("organizations.read.all", []).Should().BeNull();
        ClientCredentialsCommandHandler.GrantedScopes("organizations.read.all", MachineClients.Scopes.ConsentOnly)
            .Should().Equal("organizations.read.all");
    }
}
