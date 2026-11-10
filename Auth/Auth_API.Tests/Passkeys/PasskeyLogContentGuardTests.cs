using System.Text.RegularExpressions;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;
using Auth.Application.Features.Passkeys.BeginPasskeyAssertion;
using Auth.Application.Features.Passkeys.CompletePasskeyRegistration;
using Auth.Application.Features.Passkeys.ReauthenticateWithPasskey;
using Auth.Application.Features.Passkeys.RemovePasskey;
using Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK16): no log line of the passkey code carries a credential id, a public key, a user
/// handle, the options or the response, the challenge handle or the emailed code — identifiers
/// and reasons only. And the commands that carry a secret never print it.
/// </summary>
/// <remarks>
/// Deliberate breaks, verified red: a log call given <c>request.ChallengeHandle</c>; the ceremony
/// logging <c>userHandle</c> (REVIEW 151 F7: the names the new code gives its secrets, matched
/// whatever their case, and the calls on a <c>logger</c> parameter too); an override of the
/// controller's assertion records removed (F10, S8). REVIEW-FIXES 151 F11: the lock admission's
/// <c>logger</c> call given a fourth argument that names the user handle, and the ceremony's
/// revoked-meanwhile warning given an interpolated message (both calls hold a <c>;</c> in their
/// message, so the old matcher never read them, and both breaks stayed green under it); the old
/// argument pattern, which stops at any <c>;</c>, restored (the completeness check then names the
/// three files whose calls it skips).
/// </remarks>
public class PasskeyLogContentGuardTests
{
    /// <summary>
    /// The secrets, as stems no log argument may name: compared without regard to case with every
    /// run of whole words of an identifier, so <c>userHandle</c>, <c>ChallengeHandle</c> and
    /// <c>ownerHandle</c> are all <c>handle</c>, <c>clientDataJSON</c> is <c>clientdata</c>, and
    /// <c>rawId</c>, the credential id's name in the response, is <c>rawid</c>.
    /// </summary>
    private static readonly string[] ForbiddenStems =
    [
        "handle", "credentialid", "rawid", "publickey", "optionsjson", "responsejson", "clientdata",
        "emailcode", "idpsessiontoken", "candidate",
    ];

    // The start of a call on a field or a parameter: _logger.LogWarning( and logger.LogWarning(.
    private const string LogCallStart =
        @"(?<![A-Za-z0-9])_?logger\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\(";

    private static readonly Regex LogStart = new(LogCallStart);

    // The whole call. Its arguments run to the first ");" outside a string literal, so a ';' or a
    // ')' inside the message is text, and the arguments after it are read (REVIEW-FIXES 151 F11).
    private static readonly Regex LogCall = new(
        LogCallStart + @"(?<args>(?:""(?:[^""\\]|\\.)*""|[^;""])*?)\);",
        RegexOptions.Singleline);

    private static readonly Regex StringLiteral = new(@"""(?:[^""\\]|\\.)*""");

    // An interpolated literal: $"…", $@"…", @$"…" and the raw $"""…""", once the text is blanked.
    private static readonly Regex Interpolated = new(@"\$@?""");

    /// <summary>Every run of whole words of every identifier in <paramref name="code"/>, lower case.</summary>
    private static IEnumerable<string> NamesIn(string code)
    {
        foreach (System.Text.RegularExpressions.Match identifier in Regex.Matches(code, "[A-Za-z_][A-Za-z0-9_]*"))
        {
            var words = Regex.Split(identifier.Value, "_|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")
                .Where(word => word.Length > 0)
                .Select(word => word.ToLowerInvariant())
                .ToArray();
            for (var start = 0; start < words.Length; start++)
            {
                for (var end = start + 1; end <= words.Length; end++)
                {
                    yield return string.Concat(words[start..end]);
                }
            }
        }
    }

    /// <summary>
    /// Each log call in <paramref name="source"/> that may not stand, with why: an argument after
    /// the message template that names a secret, or a message built by interpolation, whose values
    /// sit inside the text this scan skips (structured templates only, REVIEW-FIXES 151 F11).
    /// </summary>
    private static IEnumerable<string> RefusedCalls(string source)
    {
        foreach (System.Text.RegularExpressions.Match call in LogCall.Matches(source))
        {
            // The message template is text; only the arguments after it can carry a value.
            var afterTemplate = StringLiteral.Replace(call.Groups["args"].Value, "\"\"");
            if (Interpolated.IsMatch(afterTemplate))
            {
                yield return $"an interpolated message: {call.Value}";
            }

            foreach (var secret in NamesIn(afterTemplate).Intersect(ForbiddenStems))
            {
                yield return $"{secret}: {call.Value}";
            }
        }
    }

    /// <summary>The line, counted from one, on which <paramref name="index"/> of <paramref name="source"/> stands.</summary>
    private static int LineOf(string source, int index) => source[..index].Count(character => character == '\n') + 1;

    public static TheoryData<string> PasskeySources()
    {
        var data = new TheoryData<string>();
        foreach (var file in SourceFiles())
        {
            data.Add(file);
        }

        return data;
    }

    private static List<string> SourceFiles()
    {
        var root = ApiSourceScan.SolutionDirectory();
        var files = new[]
            {
                Path.Combine(root, "Auth.Application", "Features", "Passkeys"),
                Path.Combine(root, "Auth.Infrastructure", "Authentication", "Passkeys"),
                Path.Combine(root, "Auth_API", "Common", "Passkeys"),
            }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Concat(
            [
                Path.Combine(root, "Auth.Infrastructure", "Persistence", "UserPasskeyRepository.cs"),
                Path.Combine(root, "Auth.Infrastructure", "Persistence", "WebAuthnChallengeRepository.cs"),
                Path.Combine(root, "Auth.Infrastructure", "Security", "PasskeyPostureContributor.cs"),
                Path.Combine(root, "Auth_API", "Modules", "Authentication", "Controllers", "PasskeysController.cs"),
                Path.Combine(root, "Auth_API", "Modules", "AuditLog", "EventHandlers", "PasskeyRegisteredAuditEventHandler.cs"),
                Path.Combine(root, "Auth_API", "Modules", "AuditLog", "EventHandlers", "PasskeyRemovedAuditEventHandler.cs"),
                // S20 PR B: the passkey recovery entry and the refusal at the other doors.
                Path.Combine(root, "Auth.Application", "Features", "AccountDeletion", "RecoverAccountWithPasskey", "RecoverAccountWithPasskeyCommandHandler.cs"),
                Path.Combine(root, "Auth.Application", "Features", "Authentication", "Common", "PasskeyOnlyAdministratorRule.cs"),
                // REVIEW-FIXES 152 R6: the "Confirm it's you" window's endpoints, whose passkey
                // route reads the challenge cookie and the credential.
                Path.Combine(root, "Auth_API", "Modules", "Authentication", "Controllers", "ReauthenticationController.cs"),
            ]);

        return files.Select(file => Path.GetRelativePath(root, file)).ToList();
    }

    [Theory]
    [MemberData(nameof(PasskeySources))]
    public void NoLogLine_CarriesASecretOrACredential(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), relativePath));

        RefusedCalls(source).Should().BeEmpty(
            $"{relativePath} logs identifiers and reasons only, each through a structured template");
    }

    [Theory]
    [MemberData(nameof(PasskeySources))]
    public void EveryLogCall_IsScanned(string relativePath)
    {
        // REVIEW-FIXES 151 F11: a call the matcher cannot read is a call the guard above never
        // checks, and nothing else fails; so every logger.Log…( start in the file is one call read.
        var source = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), relativePath));
        var starts = LogStart.Matches(source).Select(start => start.Index).ToList();
        var read = LogCall.Matches(source).Select(call => call.Index).ToList();

        read.Count.Should().Be(
            starts.Count,
            $"every log call in {relativePath} is read, and the calls at line(s) {string.Join(", ", starts.Except(read).Select(index => LineOf(source, index)))} are not");
    }

    [Fact]
    public void TheMatcher_ReadsPastASemicolonInTheMessage_AndRefusesAnInterpolatedOne()
    {
        // REVIEW-FIXES 151 F11, the positive controls of the two checks above: a ';' or a ')'
        // inside the message is text, so the arguments after it are read; a message built by
        // interpolation is refused whatever it holds; and the credential id's raw name is a stem.
        const string semicolon =
            "logger.LogWarning(\"{EventCode}: admitted under a {LockKind} lock (timed); nothing was lifted\", code, owner.Id, kind);";
        LogCall.Match(semicolon).Groups["args"].Value.Should().EndWith("owner.Id, kind", "the whole call is read");
        RefusedCalls(semicolon).Should().BeEmpty("identifiers and reasons are what a line may carry");
        RefusedCalls(semicolon.Replace("kind);", "kind, owner.UserHandle);")).Should().ContainSingle()
            .Which.Should().StartWith("handle: ");
        RefusedCalls("_logger.LogWarning($\"{code}: passkey {passkey.Id} ended\");").Should().ContainSingle()
            .Which.Should().StartWith("an interpolated message: ");
        LogStart.Matches(semicolon + " _logger.LogError(ex, \"x\");").Should().HaveCount(2, "a start is counted on a parameter and on a field");
        NamesIn("rawId").Should().Contain("rawid");
    }

    [Fact]
    public void TheMatcher_SeesTheCeremonysNames_AndBothKindsOfLogger()
    {
        // The positive control of the guard above: the names PR B gives its secrets, which the
        // case-sensitive word match of PR A could not see.
        NamesIn("challengeHandle, passkey.CredentialId, ownerHandle, clientDataJSON, storedPublicKey, request.ResponseJson")
            .Should().Contain(["handle", "credentialid", "clientdata", "publickey", "responsejson"]);
        NamesIn("PasskeySignalCodes.AdmittedWhileLocked, owner.Id, kind, passkey.UserId")
            .Intersect(ForbiddenStems).Should().BeEmpty("identifiers and reasons are what a line may carry");
        LogCall.IsMatch("logger.LogWarning(\"{EventCode}\", code, owner.Id);").Should().BeTrue("a logger parameter is scanned");
        LogCall.IsMatch("_logger.LogInformation(\"x {Id}\", id);").Should().BeTrue();
        LogCall.IsMatch("_myLogger.LogInformation(\"x\");").Should().BeFalse("only a logger, not a longer name that ends in it");
    }

    [Fact]
    public void TheSourcesScanned_AreThere_AndLog()
    {
        var sources = SourceFiles();
        sources.Count.Should().BeGreaterThan(10);
        sources.Select(path => File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), path)))
            .Sum(text => LogCall.Matches(text).Count)
            .Should().BeGreaterThan(5, "a scan that matches no log call proves nothing");
    }

    [Fact]
    public void TheCommands_NeverPrintTheirSecrets()
    {
        var complete = new CompletePasskeyRegistrationCommand(
            Guid.NewGuid(), Guid.NewGuid(), "secret-handle", false, "{\"secret\":\"response\"}", "Laptop", "654321");
        var remove = new RemovePasskeyCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "secret-idp-token");

        complete.ToString().Should().NotContain("secret-handle").And.NotContain("654321").And.NotContain("response");
        remove.ToString().Should().NotContain("secret-idp-token");
    }

    [Fact]
    public void TheControllersAssertionRequests_NeverPrintTheCredential()
    {
        // REVIEW 151 F10 (S8): the credential's raw text names the account and carries the signed
        // client data; as the registration request does, both records print no part of it.
        using var credential = System.Text.Json.JsonDocument.Parse(
            "{\"id\":\"secret-id\",\"rawId\":\"secret-raw\",\"response\":{\"userHandle\":\"secret-handle\",\"clientDataJSON\":\"secret-client\",\"signature\":\"secret-signature\"}}");

        var printed = new[]
        {
            new Auth_API.Modules.Authentication.Controllers.PasskeyAssertionRequest(credential.RootElement, "device-1", true).ToString(),
            new Auth_API.Modules.Authentication.Controllers.PasskeyRecoveryRequest(credential.RootElement, "device-1").ToString(),
        };

        printed.Should().OnlyContain(text => !text.Contains("secret", StringComparison.Ordinal)
            && !text.Contains("rawId", StringComparison.Ordinal)
            && !text.Contains("userHandle", StringComparison.Ordinal)
            && !text.Contains("clientDataJSON", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWindowsRecords_NeverPrintAProofOrTheCredential()
    {
        // REVIEW-FIXES 152 R6: the window's passkey command, the controller's two requests and the
        // emailed code a proof spends print identifiers only. (The password command's own print
        // test is in ReauthenticateCommandHandlerTests.)
        using var credential = System.Text.Json.JsonDocument.Parse(
            "{\"id\":\"secret-id\",\"rawId\":\"secret-raw\",\"response\":{\"userHandle\":\"secret-handle\",\"clientDataJSON\":\"secret-client\"}}");

        var printed = new[]
        {
            new ReauthenticateWithPasskeyCommand(
                Guid.NewGuid(), Guid.NewGuid(), "secret-idp-token", "secret-handle", false, "{\"secret\":\"response\"}", "203.0.113.7", "Agent")
                .ToString(),
            new Auth_API.Modules.Authentication.Controllers.ReauthenticateRequest("secret-password", "secret-email-code", "secret-second-step", true)
                .ToString(),
            new Auth_API.Modules.Authentication.Controllers.ReauthenticateWithPasskeyRequest(credential.RootElement).ToString(),
            new Auth.Domain.ValueObjects.EmailedCodeSpend(Guid.NewGuid(), "secret-code-hashes").ToString(),
        };

        printed.Should().OnlyContain(text => !text.Contains("secret", StringComparison.Ordinal)
            && !text.Contains("response", StringComparison.Ordinal)
            && !text.Contains("rawId", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSignInCommands_NeverPrintTheHandleOrTheResponse()
    {
        // S20 PR B: the challenge handle is a secret while it lives; the response names the account.
        new BeginPasskeyAssertionCommand("secret-handle", false).ToString().Should().NotContain("secret-handle");
        new PasskeyAssertionStarted("{}", "secret-handle", DateTime.UtcNow).ToString().Should().NotContain("secret-handle");
        new VerifyPasskeyAssertionCommand("secret-handle", false, "{\"secret\":\"response\"}", "203.0.113.7", "Agent", "device", true)
            .ToString().Should().NotContain("secret-handle").And.NotContain("response");
        new RecoverAccountWithPasskeyCommand("secret-handle", false, "{\"secret\":\"response\"}", "203.0.113.7", "Agent", "device")
            .ToString().Should().NotContain("secret-handle").And.NotContain("response");
    }
}
