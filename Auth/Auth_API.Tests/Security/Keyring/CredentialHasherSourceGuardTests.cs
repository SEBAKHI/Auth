using System.Text.RegularExpressions;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS3, the guard against the likeliest production failure of the keyring (the
/// pre-mortem): one lookup left on a single fingerprint works until the first
/// rotation, then fails every credential of its kind. Every lookup goes through
/// <c>Candidates</c>, every MAC check through <c>Verify</c>, and every site — the
/// issuing <c>Hash</c> calls too, whose purpose must match the lookup's — is pinned by
/// file, line and purpose (ARR-0394): adding, removing or re-purposing one fails here,
/// naming it, so the change is looked at instead of slipping through as a count that
/// still adds up. Until the first rotation a wrong purpose changes no byte, so no
/// behavioural test can see it; this list is what does.
/// </summary>
public class CredentialHasherSourceGuardTests
{
    private const string App = "Auth.Application/Features/";
    private const string Infra = "Auth.Infrastructure/";

    /// <summary>
    /// Every call on the credential hasher in production code, as
    /// <c>path:line Method Purpose</c>. Update it deliberately when a site moves; a new
    /// opaque credential takes a new purpose (KR9), issued and looked up under it.
    /// </summary>
    private static readonly string[] PinnedSites =
    [
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:358 Hash AuthorizationCode",
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:605 Hash AuthorizationRequestHandle",
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:646 Candidates AuthorizationRequestHandle",
        App + "Authentication/Common/ExternalNonceGuard.cs:64 Verify ExternalNonce",
        App + "Authentication/Common/IdpSessionReader.cs:58 Candidates IdpSession",
        App + "Authentication/Common/LoginResponseBuilder.cs:211 Hash RefreshToken",
        App + "Authentication/Common/LoginResponseBuilder.cs:411 Hash IdpSession",
        App + "Authentication/Common/TwoFactorChallengeService.cs:49 Hash TwoFactorChallenge",
        App + "Authentication/EnableTwoFactor/EnableTwoFactorCommandHandler.cs:191 Candidates IdpSession",
        App + "Authentication/ExternalLogin/IssueExternalNonceCommandHandler.cs:34 Hash ExternalNonce",
        App + "Authentication/ForgotPassword/ForgotPasswordCommandHandler.cs:111 Hash PasswordReset",
        App + "Authentication/IntrospectToken/IntrospectTokenQueryHandler.cs:145 Candidates RefreshToken",
        App + "Authentication/Logout/LogoutCommandHandler.cs:73 Candidates RefreshToken",
        App + "Authentication/LogoutWithRefreshCookie/LogoutWithRefreshCookieCommandHandler.cs:48 Candidates RefreshToken",
        App + "Authentication/RefreshToken/RefreshTokenCommandHandler.cs:79 Candidates RefreshToken",
        App + "Authentication/RefreshToken/RefreshTokenCommandHandler.cs:351 Hash RefreshToken",
        App + "Authentication/ResetPassword/ResetPasswordCommandHandler.cs:68 Candidates PasswordReset",
        App + "Authentication/RevokeToken/RevokeTokenCommandHandler.cs:180 Candidates RefreshToken",
        App + "Authentication/ServerSignOut/ServerSignOutCommandHandler.cs:94 Candidates RefreshToken",
        App + "Authentication/StepUpTwoFactor/StepUpTwoFactorCommandHandler.cs:120 Candidates IdpSession",
        App + "Authentication/TokenExchange/ExchangeAuthorizationCodeCommandHandler.cs:132 Candidates AuthorizationCode",
        App + "Authentication/VerifyTwoFactorLogin/VerifyTwoFactorLoginCommandHandler.cs:73 Candidates TwoFactorChallenge",
        App + "Organizations/AcceptInvitation/AcceptInvitationCommandHandler.cs:57 Candidates Invitation",
        App + "Organizations/GetInvitationByToken/GetInvitationByTokenQueryHandler.cs:49 Candidates Invitation",
        App + "Organizations/InviteMember/InviteMemberCommandHandler.cs:189 Hash Invitation",
        App + "Organizations/OrganizationSetup/OrganizationSetupSession.cs:157 Candidates AuthorizationRequestHandle",
        App + "Organizations/RegisterWithInvitation/RegisterWithInvitationCommandHandler.cs:98 Candidates Invitation",
        App + "Organizations/ResendInvitation/ResendInvitationCommandHandler.cs:77 Hash Invitation",
        App + "WebhookKeys/ValidateWebhookKey/ValidateWebhookKeyQueryHandler.cs:33 Candidates WebhookKey",
        Infra + "Authentication/CredentialRevocationService.cs:347 Candidates IdpSession",
        Infra + "Authentication/CredentialRevocationService.cs:380 Candidates IdpSession",
        Infra + "Authentication/CredentialRevocationService.cs:450 Candidates IdpSession",
        Infra + "Authentication/CredentialRevocationService.cs:514 Candidates IdpSession",
        Infra + "Authentication/HmacOtpHasher.cs:67 Hash PurposeOf(scope)",
        Infra + "Authentication/HmacOtpHasher.cs:90 Verify PurposeOf(scope)",
        Infra + "Authentication/StepUpTicketService.cs:60 Hash StepUpTicket",
        Infra + "Authentication/StepUpTicketService.cs:109 Verify StepUpTicket",
        Infra + "Security/PendingRegistrationHandle.cs:38 Hash PendingRegistrationHandle",
        Infra + "Security/WebhookKeyGenerator.cs:37 Hash WebhookKey",
    ];

    /// <summary>The files where a credential's MAC is checked: only through Verify.</summary>
    private static readonly string[] MacFiles =
    [
        "Auth.Application/Features/Authentication/Common/ExternalNonceGuard.cs",
        "Auth.Infrastructure/Authentication/StepUpTicketService.cs",
        "Auth.Infrastructure/Authentication/HmacOtpHasher.cs",
    ];

    /// <summary>
    /// The repositories that look a row up by an HMAC fingerprint (S11 fact 5). None
    /// filters on one value; the replay grace's stored pointer is the one exact match.
    /// </summary>
    private static readonly string[] FingerprintRepositories =
    [
        "Auth.Infrastructure/Persistence/RefreshTokenRepository.cs",
        "Auth.Infrastructure/Persistence/IdpSessionRepository.cs",
        "Auth.Infrastructure/Persistence/TwoFactorStateStore.cs",
        "Auth.Infrastructure/Persistence/AuthorizationCodeRepository.cs",
        "Auth.Infrastructure/Persistence/AuthorizationRequestRepository.cs",
        "Auth.Infrastructure/Persistence/PasswordResetTokenRepository.cs",
        "Auth.Infrastructure/Persistence/TwoFactorChallengeRepository.cs",
        "Auth.Infrastructure/Persistence/OrganizationRepository.cs",
        "Auth.Infrastructure/Persistence/WebhookKeyRepository.cs",
    ];

    // A call on the hasher, across line breaks; the purpose is read when it is a literal
    // member or the OTP scope routing, and reported as "??" otherwise (which fails).
    private static readonly Regex HasherCall = new(
        @"_credentialHasher\s*\.\s*(?<method>Hash|Candidates|Verify)\s*\(\s*(?:CredentialPurpose\.(?<purpose>\w+)|(?<purpose>PurposeOf\(scope\)))?",
        RegexOptions.Compiled);

    // Any other receiver handed a purpose: a hasher held under another name escapes the list.
    private static readonly Regex OtherReceiver = new(
        @"(?<!_credentialHasher\s*)\.\s*(?:Hash|Candidates|Verify)\s*\(\s*CredentialPurpose\.",
        RegexOptions.Compiled);

    private static readonly Regex FingerprintFilter = new(
        @"(?:WHERE|AND|OR)\s+(?:\w+\.)?\[(?:TokenHash|CodeHash|HandleHash|KeyHash|Token)\]\s*(?:=|<>)\s*@(?<param>\w+)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryHasherCall_IsAPinnedSite_WithItsPurpose()
    {
        var found = ProductionFiles()
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file.FullPath);
                return HasherCall.Matches(text).Select(match =>
                {
                    var line = text.AsSpan(0, match.Groups["method"].Index).Count('\n') + 1;
                    var purpose = match.Groups["purpose"].Success ? match.Groups["purpose"].Value : "??";
                    return $"{file.Relative}:{line} {match.Groups["method"].Value} {purpose}";
                });
            })
            .ToList();

        found.Should().HaveCountGreaterThan(30, "the scan must see the sites at all");
        found.Should().NotContain(site => site.EndsWith(" ??", StringComparison.Ordinal),
            "every call names its purpose as a CredentialPurpose member, so the list can pin it");
        found.Except(PinnedSites).Should().BeEmpty(
            "a new, moved or re-purposed call: confirm lookups use Candidates, MAC checks Verify, and that the " +
            "credential is issued and looked up under the same purpose, then update PinnedSites");
        PinnedSites.Except(found).Should().BeEmpty(
            "a pinned site disappeared: if a lookup now matches a single fingerprint, it fails after the first rotation");
    }

    [Fact]
    public void NoOtherReceiver_IsHandedACredentialPurpose()
    {
        var offenders = ProductionFiles()
            .Where(file => file.Relative != Infra + "Security/KeyringCredentialHasher.cs")
            .Where(file => OtherReceiver.IsMatch(File.ReadAllText(file.FullPath)))
            .Select(file => file.Relative)
            .ToList();

        offenders.Should().BeEmpty("the hasher is held as _credentialHasher everywhere, so every call is in the list above");
    }

    [Fact]
    public void TheReplacedKeyServices_AreGone()
    {
        var offenders = ProductionFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file.FullPath), @"\b(I?RefreshTokenKeyService|I?WebhookKeyHasher)\b"))
            .Select(file => file.Relative)
            .ToList();

        offenders.Should().BeEmpty("every fingerprint goes through ICredentialHasher");
    }

    [Fact]
    public void HmacSha256_IsComputedOnlyByTheCredentialHasher_AndTheIdentifierHasher()
    {
        // The identifier hasher keeps its own permanent key, outside the keyring (KR5).
        string[] allowed =
        [
            Infra + "Security/KeyringCredentialHasher.cs",
            Infra + "Security/IdentifierHasher.cs",
        ];

        var offenders = ProductionFiles()
            .Where(file => !allowed.Contains(file.Relative))
            .Where(file => File.ReadAllText(file.FullPath).Contains("HMACSHA256", StringComparison.Ordinal))
            .Select(file => file.Relative)
            .ToList();

        offenders.Should().BeEmpty("a keyed hash outside the hasher would not follow a key rotation");
    }

    [Fact]
    public void AMacIsNeverComparedOutsideVerify()
    {
        foreach (var file in MacFiles)
        {
            File.ReadAllText(Path.Combine(SolutionDirectory(), file)).Should().NotContain(
                "FixedTimeEquals",
                $"{file} checks a MAC: Verify compares it, in fixed time, against every accepted key");
        }
    }

    [Fact]
    public void OnlyTheHasher_IssuesCandidateSets_AndOnlyARefreshTokenBuildsAStoredHash()
    {
        // The candidate set's constructor is private to its Issuer; a second Issuer would be a
        // second way to build one from a string. The stored hash is built from a row's own
        // column, by the entity that holds it.
        var issuers = ProductionFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file.FullPath), @":\s*(?:[\w.]*\.)?CredentialHashCandidates\.Issuer\b"))
            .Select(file => file.Relative)
            .ToList();
        var storedHashBuilders = ProductionFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file.FullPath), @"new\s+StoredCredentialHash\s*\("))
            .Select(file => file.Relative)
            .ToList();

        issuers.Should().Equal(Infra + "Security/KeyringCredentialHasher.cs");
        storedHashBuilders.Should().Equal("Auth.Domain/Entities/RefreshToken.cs");
    }

    [Fact]
    public void NoRepository_FiltersOnASingleFingerprint()
    {
        var filters = FingerprintRepositories
            .SelectMany(file => FingerprintFilter.Matches(File.ReadAllText(Path.Combine(SolutionDirectory(), file)))
                .Select(match => $"{file} @{match.Groups["param"].Value}"))
            .ToList();

        filters.Should().Equal(
            ["Auth.Infrastructure/Persistence/RefreshTokenRepository.cs @StoredHash"],
            "a lookup by a presented credential takes every candidate (IN @Hashes); only the replay grace " +
            "matches a pointer exactly as another row stored it");
    }

    private static IEnumerable<(string FullPath, string Relative)> ProductionFiles()
    {
        var solution = SolutionDirectory();
        string[] projects = ["Auth.Domain", "Auth.Application", "Auth.Infrastructure", "Auth.Shared", "Auth_API", "API_Gateway"];
        var separator = Path.DirectorySeparatorChar;

        return projects
            .Select(project => Path.Combine(solution, project))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{separator}bin{separator}") && !file.Contains($"{separator}obj{separator}"))
            .Select(file => (file, Path.GetRelativePath(solution, file).Replace('\\', '/')))
            .OrderBy(file => file.Item2, StringComparer.Ordinal);
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must be able to locate Auth.sln");
        return directory!.FullName;
    }
}
