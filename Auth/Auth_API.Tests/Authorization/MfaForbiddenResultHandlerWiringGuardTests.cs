using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// S08: the API answers a withheld token's 403 with <c>TwoFactor.RequiredByPolicy</c>
/// only because <see cref="Auth_API.Authorization.MfaForbiddenResultHandler"/> is the
/// registered authorization result handler. The handler's own tests register it in
/// their host, so without this guard deleting the registration leaves every test
/// green — and the console's two-factor page and its open-tab redirect both key on
/// that code. Read from source, like the other Program.cs guards.
/// </summary>
public class MfaForbiddenResultHandlerWiringGuardTests
{
    private const string Registration =
        "builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, MfaForbiddenResultHandler>();";

    [Fact]
    public void Program_RegistersTheMfaForbiddenResultHandler()
    {
        var program = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "Program.cs"));

        Regex.Matches(program, Regex.Escape(Registration)).Should().HaveCount(1,
            "the 403 for a withheld platform authority must carry its code");
    }

    [Fact]
    public void NothingElse_RegistersAnAuthorizationResultHandler()
    {
        // The last registration is the one resolved: a second one would replace it.
        var registrations = ApiSourceScan.ProductionSources()
            .SelectMany(file => Regex.Matches(file.Source, @"<\s*IAuthorizationMiddlewareResultHandler\s*,\s*(\w+)\s*>")
                .Select(match => $"{Path.GetFileName(file.File)}: {match.Groups[1].Value}"))
            .ToList();

        registrations.Should().Equal(["Program.cs: MfaForbiddenResultHandler"]);
    }
}
