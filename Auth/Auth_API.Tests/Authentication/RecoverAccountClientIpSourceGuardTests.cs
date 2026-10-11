using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12, LK6.4: the two account-recovery endpoints read the client's address the way
/// every other endpoint does (<c>GetClientIpAddress()</c>). They used to pass
/// <c>HttpContext.Connection.RemoteIpAddress</c>, which behind the gateway is the
/// gateway's own address, so every caller shared one address: the per-address
/// ceiling that is the only bound on guessing a pending deletion's password would
/// have refused everyone, or no one.
/// </summary>
/// <remarks>Deliberate break: put <c>RemoteIpAddress</c> back in either action.</remarks>
public class RecoverAccountClientIpSourceGuardTests
{
    private static string Controller() => ApiSourceScan.ProductionSources()
        .Single(file => file.File.EndsWith(
            Path.Combine("Authentication", "Controllers", "AuthController.cs"), StringComparison.Ordinal)).Source;

    [Fact]
    public void AuthController_NeverReadsTheSocketAddress()
    {
        Controller().Should().NotContain("RemoteIpAddress");
    }

    [Theory]
    [InlineData("RecoverAccountCommand(")]
    [InlineData("RecoverAccountExternalCommand(")]
    public void RecoveryActions_PassTheResolvedClientAddress(string command)
    {
        var source = Controller();
        var start = source.IndexOf($"new {command}", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = source.IndexOf(";", start, StringComparison.Ordinal);

        Regex.IsMatch(source[start..end], @"\bGetClientIpAddress\(\)").Should().BeTrue();
    }
}
