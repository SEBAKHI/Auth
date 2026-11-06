using System.Text.RegularExpressions;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;
using Auth.Application.Features.Passkeys.BeginPasskeyAssertion;
using Auth.Application.Features.Passkeys.CompletePasskeyRegistration;
using Auth.Application.Features.Passkeys.RemovePasskey;
using Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK16): no log line of the passkey code carries a credential id, a public key, a user
/// handle, the options or the response, the challenge handle or the emailed code — identifiers
/// and reasons only. And the commands that carry a secret never print it.
/// </summary>
/// <remarks>Deliberate break, verified red: a log call given <c>request.ChallengeHandle</c>.</remarks>
public class PasskeyLogContentGuardTests
{
    private static readonly string[] Forbidden =
    [
        "CredentialId", "PublicKey", "UserHandle", "OptionsJson", "optionsJson", "ResponseJson", "responseJson",
        "ChallengeHandle", "EmailCode", "IdpSessionToken", "handle", "Candidate",
    ];

    private static readonly Regex LogCall = new(
        @"_logger\.Log(?:Trace|Debug|Information|Warning|Error|Critical)?\((?<args>(?:[^;])*?)\);",
        RegexOptions.Singleline);

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
            ]);

        return files.Select(file => Path.GetRelativePath(root, file)).ToList();
    }

    [Theory]
    [MemberData(nameof(PasskeySources))]
    public void NoLogLine_CarriesASecretOrACredential(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), relativePath));

        foreach (System.Text.RegularExpressions.Match call in LogCall.Matches(source))
        {
            // The message template is text; only the arguments after it can carry a value.
            var arguments = call.Groups["args"].Value;
            var afterTemplate = Regex.Replace(arguments, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");
            foreach (var name in Forbidden)
            {
                Regex.IsMatch(afterTemplate, $@"\b{name}\b").Should().BeFalse(
                    $"{relativePath} logs {name}: {call.Value}");
            }
        }
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
