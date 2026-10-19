using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The cookie lookup that also returns revoked SSO sessions exists for the explicit ends of a
/// browser's session only (OI-155). A caller that signs someone in through it would let a revoked
/// cookie sign a person in, so the production sources name it in exactly three places: its
/// declaration, its implementation, and one call inside <c>CredentialRevocationService</c>.
/// </summary>
public class RevokedIdpSessionLookupGuardTests
{
    private const string Lookup = "GetByTokenHashIncludingRevokedAsync";

    [Fact]
    public void OnlyTheRevocationService_CallsTheLookupThatIncludesRevokedSessions()
    {
        var sources = ApiSourceScan.ProductionSources().ToList();

        // A floor, so the guard cannot pass because it stopped seeing the sources.
        sources.Should().HaveCountGreaterThan(100);

        sources
            .Where(source => source.Source.Contains(Lookup, StringComparison.Ordinal))
            .Select(source => Path.GetFileName(source.File))
            .Should().BeEquivalentTo(["IIdpSessionRepository.cs", "IdpSessionRepository.cs", "CredentialRevocationService.cs"],
                "a reader that signs browsers in must keep the live-only lookup");

        var service = sources.Single(source => Path.GetFileName(source.File) == "CredentialRevocationService.cs").Source;
        Regex.Matches(service, $@"\b{Lookup}\(").Should().ContainSingle(
            "one call, inside FindIdpSessionNamedByCookieAsync");
    }
}
