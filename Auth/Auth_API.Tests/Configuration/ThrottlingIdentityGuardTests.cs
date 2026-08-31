namespace Auth_API.Tests.Configuration;

/// <summary>
/// Source-level guards over three arrangements that are correct only in relation to
/// each other, and that no behavioural test can see because each half looks fine on
/// its own.
///
/// <para>
/// The defect these lock down: the "apikey-validate" policy documented itself as
/// partitioning per caller, then read <c>ClaimTypes.NameIdentifier</c> — a claim type
/// this process guarantees is never present, because it clears
/// <c>DefaultInboundClaimTypeMap</c> and sets <c>MapInboundClaims = false</c> so claims
/// keep their JWT names. The read returned null on every request and the policy fell
/// through to its IP fallback for its entire life. The limiter also ran ahead of
/// <c>UseAuthentication</c>, so even the correct claim would have been absent. Two
/// independent causes, one silent outcome, and a comment above them describing the
/// behaviour everybody assumed was happening.
/// </para>
///
/// <para>
/// These read the source text rather than exercising the pipeline. That is the point:
/// the arrangement is what is being asserted, and a runtime test of a correct pipeline
/// passes just as happily against a broken one whose fallback quietly covers for it.
/// </para>
/// </summary>
public class ThrottlingIdentityGuardTests
{
    private static string ReadSource(params string[] relativeParts)
        => File.ReadAllText(Path.Combine(SolutionDirectory(), Path.Combine(relativeParts)));

    private static string ApiProgram() => ReadSource("Auth_API", "Program.cs");

    private static string GatewayProgram() => ReadSource("API_Gateway", "Program.cs");

    [Fact]
    public void RateLimiter_RunsAfterAuthentication_SoCallerPartitionedPoliciesSeeAPrincipal()
    {
        var program = ApiProgram();

        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var rateLimiter = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);

        authentication.Should().BeGreaterThan(0, "the pipeline must call UseAuthentication");
        rateLimiter.Should().BeGreaterThan(0, "the pipeline must call UseRateLimiter");

        rateLimiter.Should().BeGreaterThan(authentication,
            "a limiter ahead of authentication sees only the anonymous principal, so every " +
            "policy that partitions on the caller silently degrades to its IP fallback");
    }

    [Fact]
    public void RateLimiter_RunsBeforeAuthorization_SoThrottlingStillPrecedesTheEndpoint()
    {
        var program = ApiProgram();

        var rateLimiter = program.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        authorization.Should().BeGreaterThan(0, "the pipeline must call UseAuthorization");

        rateLimiter.Should().BeLessThan(authorization,
            "the whole purpose of the limiter is to reject a request before the endpoint does work");
    }

    [Fact]
    public void RateLimitPartitions_NeverReadClaimTypesNameIdentifier()
    {
        var program = ApiProgram();

        // Comments are allowed to name it — they explain why it is wrong. Only the
        // executable references matter, and the sole way this file ever produced one
        // was through a claim lookup.
        program.Should().NotContain("FindFirst(ClaimTypes.NameIdentifier)",
            "this process clears DefaultInboundClaimTypeMap and sets MapInboundClaims = false, " +
            "so the SOAP-era ClaimTypes.NameIdentifier URI is never present on any principal here; " +
            "the subject claim is \"sub\"");
    }

    [Fact]
    public void ApiKeyValidatePolicy_PartitionsOnTheSubjectClaim()
    {
        var program = ApiProgram();

        var policy = program.IndexOf("options.AddPolicy(\"apikey-validate\"", StringComparison.Ordinal);
        policy.Should().BeGreaterThan(0, "the apikey-validate policy must be registered");

        // The partition key is built in the few lines directly after the registration.
        var body = program[policy..Math.Min(program.Length, policy + 600)];

        body.Should().Contain("JwtClaimNames.Subject",
            "the endpoint is authenticated and the comment above it promises a per-caller budget; " +
            "an IP partition lets one busy integration throttle every other one behind the same NAT");
    }

    [Fact]
    public void MapInboundClaims_StaysDisabled_BecauseTheGuardsAboveDependOnIt()
    {
        var program = ApiProgram();

        program.Should().Contain("JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();");
        program.Should().Contain("options.MapInboundClaims = false;");
    }

    [Theory]
    [InlineData("AddXForwardedFor")]
    [InlineData("AddXForwardedHost")]
    [InlineData("AddXForwardedProto")]
    public void Gateway_ForwardedHeaderTransforms_StateTheActionExplicitly(string transform)
    {
        var program = GatewayProgram();

        program.Should().Contain($"{transform}(action: ForwardedTransformActions.Set)",
            "the Auth API takes the FIRST entry of X-Forwarded-For as the client address and " +
            "partitions its login and password-reset limiters on it, so whether that entry is " +
            "trustworthy is decided by this transform. Append would preserve the caller's own " +
            "value in front of the observed one and hand every client the power to pick its " +
            "rate-limit bucket and to write any address it likes into the audit log. That is far " +
            "too load-bearing to rest on a default parameter in a dependency");
    }

    [Fact]
    public void GatewayCors_RefusesTheWildcardOutsideDevelopment()
    {
        var provider = ReadSource("API_Gateway", "Configuration", "DynamicCorsPolicyProvider.cs");

        provider.Should().Contain("_environment.IsDevelopment()",
            "this process is the edge browsers actually talk to, so a wildcard accepted here is " +
            "the one that reaches real users; the Auth API's provider of the same name has always " +
            "gated it on Development and the two must agree");

        // Ordering, not just presence: the environment check must guard the wildcard
        // branch rather than sit somewhere else in the file.
        var guard = provider.IndexOf("_environment.IsDevelopment()", StringComparison.Ordinal);
        var wildcard = provider.IndexOf("builder.AllowAnyOrigin()", StringComparison.Ordinal);

        wildcard.Should().BeGreaterThan(0, "the wildcard branch still exists for Development");
        guard.Should().BeLessThan(wildcard,
            "an ungated AllowAnyOrigin is exactly what this guard exists to prevent");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
