using System.Text.RegularExpressions;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS3, the guard against the likeliest production failure of the keyring (the
/// pre-mortem): one lookup left on a single fingerprint works until the first
/// rotation, then fails every credential of its kind. Every lookup goes through
/// <c>Candidates</c> and every MAC check through <c>Verify</c>, and the sites are
/// pinned by file and line (ARR-0394): adding or removing one fails here, naming it,
/// so the change is looked at instead of slipping through as a count that still adds up.
/// </summary>
public class CredentialHasherSourceGuardTests
{
    private const string App = "Auth.Application/Features/";

    /// <summary>
    /// Every <c>Candidates</c> and <c>Verify</c> call in production code, as
    /// <c>path:line Method Purpose</c>. Update it deliberately when a site moves; a new
    /// opaque credential takes a new purpose (KR9).
    /// </summary>
    private static readonly string[] PinnedSites =
    [
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:646 Candidates AuthorizationRequestHandle",
        App + "Authentication/Common/ExternalNonceGuard.cs:64 Verify ExternalNonce",
        App + "Authentication/Common/IdpSessionReader.cs:58 Candidates IdpSession",
        App + "Authentication/EnableTwoFactor/EnableTwoFactorCommandHandler.cs:191 Candidates IdpSession",
        App + "Authentication/IntrospectToken/IntrospectTokenQueryHandler.cs:145 Candidates RefreshToken",
        App + "Authentication/Logout/LogoutCommandHandler.cs:73 Candidates RefreshToken",
        App + "Authentication/LogoutWithRefreshCookie/LogoutWithRefreshCookieCommandHandler.cs:48 Candidates RefreshToken",
        App + "Authentication/RefreshToken/RefreshTokenCommandHandler.cs:79 Candidates RefreshToken",
        App + "Authentication/ResetPassword/ResetPasswordCommandHandler.cs:68 Candidates PasswordReset",
        App + "Authentication/RevokeToken/RevokeTokenCommandHandler.cs:180 Candidates RefreshToken",
        App + "Authentication/ServerSignOut/ServerSignOutCommandHandler.cs:94 Candidates RefreshToken",
        App + "Authentication/StepUpTwoFactor/StepUpTwoFactorCommandHandler.cs:120 Candidates IdpSession",
        App + "Authentication/TokenExchange/ExchangeAuthorizationCodeCommandHandler.cs:132 Candidates AuthorizationCode",
        App + "Authentication/VerifyTwoFactorLogin/VerifyTwoFactorLoginCommandHandler.cs:73 Candidates TwoFactorChallenge",
        App + "Organizations/AcceptInvitation/AcceptInvitationCommandHandler.cs:57 Candidates Invitation",
        App + "Organizations/GetInvitationByToken/GetInvitationByTokenQueryHandler.cs:49 Candidates Invitation",
        App + "Organizations/OrganizationSetup/OrganizationSetupSession.cs:157 Candidates AuthorizationRequestHandle",
        App + "Organizations/RegisterWithInvitation/RegisterWithInvitationCommandHandler.cs:98 Candidates Invitation",
        App + "WebhookKeys/ValidateWebhookKey/ValidateWebhookKeyQueryHandler.cs:33 Candidates WebhookKey",
        "Auth.Infrastructure/Authentication/CredentialRevocationService.cs:347 Candidates IdpSession",
        "Auth.Infrastructure/Authentication/CredentialRevocationService.cs:380 Candidates IdpSession",
        "Auth.Infrastructure/Authentication/CredentialRevocationService.cs:450 Candidates IdpSession",
        "Auth.Infrastructure/Authentication/CredentialRevocationService.cs:514 Candidates IdpSession",
        "Auth.Infrastructure/Authentication/HmacOtpHasher.cs:90 Verify PurposeOf(scope)",
        "Auth.Infrastructure/Authentication/StepUpTicketService.cs:109 Verify StepUpTicket",
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

    private static readonly Regex SiteCall = new(
        @"\.(?<method>Candidates|Verify)\((?:CredentialPurpose\.(?<purpose>\w+)|(?<purpose>PurposeOf\(scope\)))",
        RegexOptions.Compiled);

    private static readonly Regex FingerprintFilter = new(
        @"(?:WHERE|AND|OR)\s+(?:\w+\.)?\[(?:TokenHash|CodeHash|HandleHash|KeyHash|Token)\]\s*(?:=|<>)\s*@(?<param>\w+)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryLookupAndMacCheck_IsAPinnedSite()
    {
        var found = ProductionFiles()
            .SelectMany(file => File.ReadAllLines(file.FullPath)
                .Select((line, index) => (file.Relative, Line: index + 1, Match: SiteCall.Match(line))))
            .Where(hit => hit.Match.Success)
            .Select(hit => $"{hit.Relative}:{hit.Line} {hit.Match.Groups["method"].Value} {hit.Match.Groups["purpose"].Value}")
            .ToList();

        found.Should().NotBeEmpty("the scan must see the sites at all");
        found.Except(PinnedSites).Should().BeEmpty(
            "a new lookup or MAC check, or one that moved: confirm it goes through Candidates (lookups) or " +
            "Verify (MACs) under its own purpose, then update PinnedSites");
        PinnedSites.Except(found).Should().BeEmpty(
            "a pinned site disappeared: if a lookup now matches a single fingerprint, it fails after the first rotation");
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
            "Auth.Infrastructure/Security/KeyringCredentialHasher.cs",
            "Auth.Infrastructure/Security/IdentifierHasher.cs",
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
    public void ACandidateSet_IsBuiltOnlyByTheHasher()
    {
        var offenders = ProductionFiles()
            .Where(file => file.Relative != "Auth.Infrastructure/Security/KeyringCredentialHasher.cs")
            .Where(file => File.ReadAllText(file.FullPath).Contains("new CredentialHashCandidates(", StringComparison.Ordinal))
            .Select(file => file.Relative)
            .ToList();

        offenders.Should().BeEmpty();
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
            .Select(file => (file, Path.GetRelativePath(solution, file).Replace('\\', '/')));
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
