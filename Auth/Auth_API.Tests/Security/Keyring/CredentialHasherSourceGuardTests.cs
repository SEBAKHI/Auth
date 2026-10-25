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
        // S24: the challenge a recovery's emailed code is checked against needs a unique
        // fingerprint in TokenHash; its token is never handed to anyone.
        App + "AccountDeletion/Common/AccountDeletionRecoverer.cs:204 Hash TwoFactorChallenge",
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:358 Hash AuthorizationCode",
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:605 Hash AuthorizationRequestHandle",
        App + "Authentication/Authorize/AuthorizeCommandHandler.cs:646 Candidates AuthorizationRequestHandle",
        // S24 PR B: the challenges of switching the emailed code on and off are answered
        // by the signed-in account; their TokenHash, like a recovery's, is a unique
        // fingerprint whose token is never handed to anyone. One site opens both.
        App + "Authentication/Common/AccountEmailCodeChallenge.cs:50 Hash TwoFactorChallenge",
        App + "Authentication/Common/ExternalNonceGuard.cs:64 Verify ExternalNonce",
        App + "Authentication/Common/IdpSessionReader.cs:58 Candidates IdpSession",
        App + "Authentication/Common/LoginResponseBuilder.cs:211 Hash RefreshToken",
        App + "Authentication/Common/LoginResponseBuilder.cs:411 Hash IdpSession",
        App + "Authentication/Common/TwoFactorChallengeService.cs:71 Hash TwoFactorChallenge",
        App + "Authentication/EnableTwoFactor/EnableTwoFactorCommandHandler.cs:246 Candidates IdpSession",
        App + "Authentication/ExternalLogin/IssueExternalNonceCommandHandler.cs:34 Hash ExternalNonce",
        App + "Authentication/ForgotPassword/ForgotPasswordCommandHandler.cs:111 Hash PasswordReset",
        App + "Authentication/IntrospectToken/IntrospectTokenQueryHandler.cs:145 Candidates RefreshToken",
        App + "Authentication/Logout/LogoutCommandHandler.cs:73 Candidates RefreshToken",
        App + "Authentication/LogoutWithRefreshCookie/LogoutWithRefreshCookieCommandHandler.cs:48 Candidates RefreshToken",
        App + "Authentication/RefreshToken/RefreshTokenCommandHandler.cs:79 Candidates RefreshToken",
        App + "Authentication/RefreshToken/RefreshTokenCommandHandler.cs:351 Hash RefreshToken",
        App + "Authentication/ResetPassword/ResetPasswordCommandHandler.cs:74 Candidates PasswordReset",
        App + "Authentication/RevokeToken/RevokeTokenCommandHandler.cs:180 Candidates RefreshToken",
        // S24: the anonymous send of an emailed second-factor code, which finds its sign-in
        // challenge by the token exactly as verify does.
        App + "Authentication/SendSignInEmailCode/SendSignInEmailCodeCommandHandler.cs:63 Candidates TwoFactorChallenge",
        App + "Authentication/ServerSignOut/ServerSignOutCommandHandler.cs:94 Candidates RefreshToken",
        App + "Authentication/StepUpTwoFactor/StepUpTwoFactorCommandHandler.cs:120 Candidates IdpSession",
        App + "Authentication/TokenExchange/ExchangeAuthorizationCodeCommandHandler.cs:132 Candidates AuthorizationCode",
        App + "Authentication/VerifyTwoFactorLogin/VerifyTwoFactorLoginCommandHandler.cs:79 Candidates TwoFactorChallenge",
        App + "Organizations/AcceptInvitation/AcceptInvitationCommandHandler.cs:57 Candidates Invitation",
        App + "Organizations/GetInvitationByToken/GetInvitationByTokenQueryHandler.cs:49 Candidates Invitation",
        App + "Organizations/InviteMember/InviteMemberCommandHandler.cs:187 Hash Invitation",
        App + "Organizations/OrganizationSetup/OrganizationSetupSession.cs:157 Candidates AuthorizationRequestHandle",
        App + "Organizations/RegisterWithInvitation/RegisterWithInvitationCommandHandler.cs:98 Candidates Invitation",
        App + "Organizations/ResendInvitation/ResendInvitationCommandHandler.cs:77 Hash Invitation",
        App + "WebhookKeys/ValidateWebhookKey/ValidateWebhookKeyQueryHandler.cs:43 Candidates WebhookKey",
        // S11 PR B: the issuing form a matched webhook key is compared with, to re-fingerprint it.
        App + "WebhookKeys/ValidateWebhookKey/ValidateWebhookKeyQueryHandler.cs:54 Hash WebhookKey",
        Infra + "Authentication/CredentialRevocationService.cs:347 Candidates IdpSession",
        Infra + "Authentication/CredentialRevocationService.cs:380 Candidates IdpSession",
        Infra + "Authentication/CredentialRevocationService.cs:450 Candidates IdpSession",
        Infra + "Authentication/CredentialRevocationService.cs:514 Candidates IdpSession",
        // The OTP hasher routes by scope prefix: Otp, TwoFactorBind and, from S24, TwoFactorEmailCode.
        Infra + "Authentication/HmacOtpHasher.cs:71 Hash PurposeOf(scope)",
        Infra + "Authentication/HmacOtpHasher.cs:94 Verify PurposeOf(scope)",
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
    /// Every single-value filter on a fingerprint-like column that is NOT a lookup by a
    /// presented credential under the keyring, as <c>file [Column] @Parameter</c>, each with
    /// why. Any other <c>WHERE/AND/OR/ON [*Hash|*Token|*Handle] = @x</c> under Persistence
    /// fails the guard: a new table (S24 adds one) must look its rows up by candidates.
    /// </summary>
    private static readonly string[] SingleValueFilterAllowList =
    [
        // The deletion registry: the identifier hasher's own permanent key, outside the keyring (KR5).
        "AccountDeletionTombstoneRepository.cs [EmailHash] @EmailHash",
        // API keys: an Argon2id hash looked up by its prefix's row, not an HMAC fingerprint.
        "ApiKeyRepository.cs [KeyHash] @KeyHash",
        // The pending-registration handle the client was answered with is the stored value itself,
        // and the OTP-hash match is a concurrency guard on the value the same request just read.
        "PendingRegistrationRepository.cs [Handle] @Handle",
        "PendingRegistrationRepository.cs [OtpHash] @OtpHash",
        // The replay grace: a pointer exactly as another row stored it (StoredCredentialHash).
        "RefreshTokenRepository.cs [TokenHash] @StoredHash",
        // Known devices: an unkeyed SHA-256 of the device, not a credential.
        "UserKnownDeviceRepository.cs [DeviceHash] @DeviceHash",
        "UserKnownDeviceRepository.cs [DeviceHash] @LegacyHash",
        "UserKnownDeviceRepository.cs [DeviceHash] @CurrentHash",
        "UserSessionRepository.cs [DeviceHash] @DeviceHash",
        // A password-hash concurrency guard on the Argon2id value the same request verified.
        "UserRepository.cs [PasswordHash] @VerifiedHash",
        // The webhook re-fingerprint (S11 PR B): a concurrency guard on the stored value the same
        // validation just matched, never a lookup by a presented key.
        "WebhookKeyRepository.cs [KeyHash] @MatchedHash",
        // IUserSessionRepository.GetByTokenHashAsync has no caller (S11 fact 5); left as it is.
        "UserSessionRepository.cs [SessionToken] @SessionToken",
        // S24: the emailed code is spent only while its challenge still holds the hashes the
        // same request checked the code against — a concurrency guard on the stored text, like
        // the recovery codes' set; the challenge itself was found by its id.
        "TwoFactorStateStore.cs [EmailCodeHash] @CodesSeen",
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

    // A filter, not an assignment: after WHERE, AND, OR or ON, on a column named like a fingerprint.
    private static readonly Regex FingerprintFilter = new(
        @"(?:WHERE|AND|OR|ON)\s+(?:\w+\.)?\[(?<column>\w*(?:Hash|Token|Handle))\]\s*(?:=|<>)\s*@(?<param>\w+)",
        RegexOptions.Compiled);

    // Any HMAC construction .NET offers, not only the one the hasher uses.
    private static readonly Regex HmacConstruction = new(
        @"HMACSHA\d+|HMACMD5|\bHMAC\s*\.\s*Create|IncrementalHash\s*\.\s*CreateHMAC|\bKeyedHashAlgorithm\b",
        RegexOptions.Compiled);

    // A credential hasher held under any name other than _credentialHasher (field) or
    // credentialHasher (the constructor parameter that fills it).
    private static readonly Regex OtherHasherName = new(
        @"\bICredentialHasher\??\s+(?!_credentialHasher\b|credentialHasher\b)(?<name>\w+)\s*[;=,)]",
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
    public void TheHasher_IsHeldUnderOneName()
    {
        // A field or parameter of another name could be called with a purpose held in a
        // variable, which neither list above would see.
        var offenders = ProductionFiles()
            .SelectMany(file => OtherHasherName.Matches(File.ReadAllText(file.FullPath))
                .Select(match => $"{file.Relative} {match.Groups["name"].Value}"))
            .ToList();

        offenders.Should().BeEmpty("hold ICredentialHasher as _credentialHasher (or the credentialHasher constructor parameter)");
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
    public void AnHmac_IsComputedOnlyByTheCredentialHasher_AndTheIdentifierHasher()
    {
        // The identifier hasher keeps its own permanent key, outside the keyring (KR5).
        string[] allowed =
        [
            Infra + "Security/KeyringCredentialHasher.cs",
            Infra + "Security/IdentifierHasher.cs",
        ];

        var offenders = ProductionFiles()
            .Where(file => !allowed.Contains(file.Relative))
            .Where(file => HmacConstruction.IsMatch(File.ReadAllText(file.FullPath)))
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
    public void NoRepository_FiltersOnASingleFingerprint_ExceptTheAllowedLookups()
    {
        var persistence = Path.Combine(SolutionDirectory(), "Auth.Infrastructure", "Persistence");
        var filters = Directory.EnumerateFiles(persistence, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => FingerprintFilter.Matches(File.ReadAllText(file))
                .Select(match => $"{Path.GetRelativePath(persistence, file).Replace('\\', '/')} [{match.Groups["column"].Value}] @{match.Groups["param"].Value}"))
            .Distinct()
            .ToList();

        filters.Should().NotBeEmpty("the scan must see the persistence files at all");
        filters.Except(SingleValueFilterAllowList).Should().BeEmpty(
            "a lookup by a presented credential takes every candidate (IN @Hashes); a filter on one value " +
            "works until the first rotation. If it is truly not a keyring credential, add it to the allow-list with why");
        SingleValueFilterAllowList.Except(filters).Should().BeEmpty("an allow-list entry no longer exists: remove it");
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
