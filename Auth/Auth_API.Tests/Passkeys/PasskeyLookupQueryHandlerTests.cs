using System.Text.RegularExpressions;
using Auth.Application.Features.Authentication.StartEmailCodeSignIn;
using Auth.Application.Features.Passkeys.PasskeyLookup;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// OI-280 (T-A1, D-S20-10): the sign-in page's one question after the address. The handler asks
/// the repository once, for the address and the RP ID in force, and answers what it says; while
/// passkeys do not run it answers false and reads no passkey. Which rows count is the statement's
/// (PasskeyStoreSqlTextTests, and PasskeyDevRun's <c>lookup</c> scenario on a real engine).
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the RP ID in force replaced by a constant; the
/// repository asked while passkeys do not run; the address logged raw by the handler (the log
/// guard below and PasskeyLookupEndpointTests' captured lines).
/// </remarks>
public class PasskeyLookupQueryHandlerTests
{
    private const string Address = "ada@example.com";

    private readonly Mock<IUserPasskeyRepository> _passkeys = new(MockBehavior.Strict);

    private PasskeyLookupQueryHandler Handler(bool enabled = true, string rpId = PasskeyTestDoubles.RpId) =>
        new(PasskeyTestDoubles.Feature(enabled: enabled, rpId: rpId), _passkeys.Object);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhilePasskeysRun_AnswersWhatTheRepositorySaysForTheAddressAndTheRpIdInForce(bool holds)
    {
        _passkeys.Setup(r => r.HasLiveForEmailAsync(Address, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(holds);

        var result = await Handler().Handle(new PasskeyLookupQuery(Address), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(holds);
        _passkeys.Verify(r => r.HasLiveForEmailAsync(Address, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()), Times.Once,
            "one read for every address");
    }

    [Theory]
    [InlineData(false, PasskeyTestDoubles.RpId)]
    [InlineData(true, "")]
    public async Task Handle_WhilePasskeysDoNotRun_AnswersFalse_AndReadsNoPasskey(bool enabled, string rpId)
    {
        // Switched off between the gate and this read, or no RP ID: an address without a
        // passkey's answer. The strict mock refuses any read of the passkeys.
        var result = await Handler(enabled, rpId).Handle(new PasskeyLookupQuery(Address), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PassesTheCallersCancellation_ToTheRead()
    {
        using var source = new CancellationTokenSource();
        _passkeys.Setup(r => r.HasLiveForEmailAsync(Address, PasskeyTestDoubles.RpId, source.Token)).ReturnsAsync(false);

        (await Handler().Handle(new PasskeyLookupQuery(Address), source.Token)).Value.Should().BeFalse();
    }

    // ── The address, as the code sign-in's start checks it ──

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("a@b@c")]
    public void Validator_RefusesAMalformedAddress_WithTheCodeSignInStartsCodes(string email)
    {
        var lookup = new PasskeyLookupQueryValidator().Validate(new PasskeyLookupQuery(email));
        var codeStart = new StartEmailCodeSignInCommandValidator()
            .Validate(new StartEmailCodeSignInCommand(email, null, "203.0.113.7", "Agent"));

        lookup.IsValid.Should().BeFalse();
        lookup.Errors.Select(error => error.ErrorCode)
            .Should().Equal(codeStart.Errors.Where(error => error.PropertyName == "Email").Select(error => error.ErrorCode),
                "a malformed address gets the same 400 at both doors");
    }

    [Fact]
    public void Validator_RefusesAnAddressLongerThanAnyAccountCanHold()
    {
        var tooLong = new string('a', 250) + "@example.com";

        new PasskeyLookupQueryValidator().Validate(new PasskeyLookupQuery(tooLong)).IsValid.Should().BeFalse();
        new PasskeyLookupQueryValidator().Validate(new PasskeyLookupQuery(Address)).IsValid.Should().BeTrue();
    }

    // ── The address in logs (PK16 and its address rule, V4) ──

    [Fact]
    public void TheQuery_NeverPrintsTheAddress()
    {
        new PasskeyLookupQuery(Address).ToString().Should().NotContain("ada").And.NotContain("example");
        new Auth_API.Modules.Authentication.Controllers.PasskeyLookupRequest(Address).ToString()
            .Should().NotContain("ada").And.NotContain("example");
    }

    /// <summary>
    /// No line of the lookup's code names the address unless masked, and none at Information or
    /// above: one line per answer would be a record of who holds a passkey. Read from the source,
    /// so a line that only some answers write is seen too.
    /// </summary>
    [Fact]
    public void TheLookupsCode_LogsNoAddress_AndNoLinePerAnswer()
    {
        var root = ApiSourceScan.SolutionDirectory();
        var sources = Directory.EnumerateFiles(
                Path.Combine(root, "Auth.Application", "Features", "Passkeys", "PasskeyLookup"), "*.cs")
            .Select(File.ReadAllText)
            .Append(ActionSource(File.ReadAllText(
                Path.Combine(root, "Auth_API", "Modules", "Authentication", "Controllers", "PasskeysController.cs"))))
            .ToList();

        sources.Should().HaveCount(4, "the query, its handler, its validator and the controller's action are read");

        var calls = sources.SelectMany(source => LogCall.Matches(source)).Select(call => call.Value).ToList();

        calls.Where(call => PerAnswerLevel.IsMatch(call))
            .Should().BeEmpty("a line per lookup at Information or above would be a record of who holds a passkey");
        calls.Where(call => NamesTheAddress(call))
            .Should().BeEmpty("the address is personal data: masked, or not logged");
    }

    [Fact]
    public void TheLogGuard_SeesARawAddress_AndAPerAnswerLine()
    {
        // The positive controls of the guard above.
        const string raw = "_logger.LogDebug(\"Lookup for {Email}\", request.Email);";
        const string masked = "_logger.LogDebug(\"Lookup for {Email}\", EmailMasking.Mask(request.Email));";
        const string perAnswer = "_logger.LogInformation(\"Lookup answered\");";

        LogCall.IsMatch(raw).Should().BeTrue();
        NamesTheAddress(LogCall.Match(raw).Value).Should().BeTrue();
        NamesTheAddress(LogCall.Match(masked).Value).Should().BeFalse();
        PerAnswerLevel.IsMatch(LogCall.Match(perAnswer).Value).Should().BeTrue();
    }

    private static readonly Regex LogCall = new(
        @"(?<![A-Za-z0-9])_?logger\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\((?:""(?:[^""\\]|\\.)*""|[^;""])*?\);",
        RegexOptions.Singleline);

    private static readonly Regex PerAnswerLevel = new(@"\.Log(?:Information|Warning|Error|Critical)?\(");

    /// <summary>Whether a log call's arguments name the address outside <c>EmailMasking.Mask(…)</c>.</summary>
    private static bool NamesTheAddress(string call)
    {
        var arguments = Regex.Replace(call, @"""(?:[^""\\]|\\.)*""", "\"\"");
        arguments = Regex.Replace(arguments, @"EmailMasking\.Mask\([^()]*(?:\([^()]*\)[^()]*)*\)", "masked");
        return Regex.IsMatch(arguments, @"(?i)email|address");
    }

    /// <summary>The controller's lookup action, from its route attribute to the next action's.</summary>
    private static string ActionSource(string controller)
    {
        var start = controller.IndexOf("[HttpPost(\"lookup\")]", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the lookup action must exist");
        var end = controller.IndexOf("[HttpPost(\"assertion/options\")]", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return controller[start..end];
    }
}
