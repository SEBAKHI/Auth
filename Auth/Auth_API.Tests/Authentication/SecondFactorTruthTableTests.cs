using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Dashboard.GetUserStats;
using Auth.Application.Features.Users.GetUserById;
using Auth.Application.Features.Users.GetUsers;
using Auth.Application.Features.Users.UpdateProfile;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Dashboard;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// D2 (EO2): what each set of methods a sign-in can prove counts for — the one table
/// the emailed second factor must never move. A platform administrator whose only
/// factor is the emailed code is asked to enroll one that counts.
/// </summary>
public class SecondFactorTruthTableTests
{
    private static readonly AuthenticationMethods Pwd = AuthenticationMethods.Password;

    public static TheoryData<string, AuthenticationMethods, bool, string[]> Rows => new()
    {
        { "password alone", Pwd, false, ["pwd"] },
        { "password + authenticator", Pwd.With(AuthenticationMethods.Totp), true, ["pwd", "otp", "mfa"] },
        { "password + qualified recovery code", Pwd.With(SecondFactorProof.RecoveryCode("[\"a\"]", "[]", qualified: true).Methods), true, ["pwd", "mfa"] },
        { "password + emailed code", Pwd.With(SecondFactorProof.EmailCode(Guid.NewGuid(), "m").Methods), false, ["pwd"] },
        { "password + unqualified recovery code", Pwd.With(SecondFactorProof.RecoveryCode("[\"a\"]", "[]", qualified: false).Methods), false, ["pwd"] },
        { "provider + emailed code", AuthenticationMethods.ExternalIdentity.With(AuthenticationMethods.EmailCode), false, [] },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void EachSet_CountsForExactlyWhatD2Says(string row, AuthenticationMethods methods, bool mfa, string[] amr)
    {
        methods.IsMfaSatisfied.Should().Be(mfa, row);
        methods.ToAmrValues().Should().Equal(amr, row + ": an emailed code is never \"otp\" nor \"mfa\"");
    }

    [Fact]
    public void AnEmailOnlyPlatformAdministrator_IsAskedToEnroll_NeverToStepUp()
    {
        var policy = new PlatformMfaPolicy(
            Mock.Of<ITwoFactorStateStore>(),
            Mock.Of<ITokenClaimsResolver>(),
            TestHelpers.CreateOptions(new TwoFactorSettings { EnforceForPlatformAdmins = true }),
            TestHelpers.LoadedSettingsReloader(),
            new EnforcedSettingsWarning(),
            Mock.Of<ILogger<PlatformMfaPolicy>>());
        var claims = new TokenClaims(["admin"], ["users:read"], []);

        // The emailed code's row has IsEnabled = 0, which is what the policy reads.
        var decision = policy.Apply(claims, Pwd.With(AuthenticationMethods.EmailCode), hasEnabledSecondFactor: false, enforce: true);

        decision.Requirement.Should().Be(MfaRequirement.Enroll);
        decision.Claims.Permissions.Should().BeEmpty();
    }
}

/// <summary>
/// FA2: "the account has a second factor that counts" is an enabled authenticator app
/// and nothing else; the database refuses a row that would make the emailed code look
/// like one.
/// </summary>
public class SecondFactorInventoryEmailTests
{
    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    [Fact]
    public async Task TheFactorThePolicyReads_IsAnEnabledRow_AndNeverTheEmailedCode()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new TwoFactorStateStore(db).HasEnabledFactorAsync(Guid.NewGuid(), CancellationToken.None);

        Sql(db.Commands.Should().ContainSingle().Subject).Should().Be(
            "SELECT CAST(CASE WHEN EXISTS ( SELECT 1 FROM dbo.TwoFactorAuth WHERE UserId = @UserId AND IsEnabled = 1) THEN 1 ELSE 0 END AS BIT)");
    }

    [Fact]
    public void TheTable_RefusesBothFactorsOnOneRow_AndAnEnabledRowWithoutASecret()
    {
        var table = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "TwoFactorAuth.sql"));

        table.Should().Contain("CONSTRAINT [CK_TwoFactorAuth_SingleFactor] CHECK (NOT ([IsEnabled] = 1 AND [EmailCodeEnabledAt] IS NOT NULL))");
        table.Should().Contain("CONSTRAINT [CK_TwoFactorAuth_EnabledHasSecret] CHECK ([IsEnabled] = 0 OR [SecretKey] IS NOT NULL)");
        table.Should().Contain("[SecretKey] NVARCHAR(500) NULL,");
    }

    /// <summary>
    /// S24 PR B, OI-220 (7) (SEC-N5): a row always holds a factor — an authenticator
    /// secret or the emailed code. Switching the emailed code off without an app taking
    /// its place, or the reverse half-done, is refused by the database itself; switching
    /// a factor off deletes the row. Named and the table's last constraint, so the
    /// publish appends it.
    /// </summary>
    [Fact]
    public void TheTable_RefusesARowWithNoFactor_AsItsLastConstraint()
    {
        var table = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "TwoFactorAuth.sql"));

        const string HasFactor =
            "CONSTRAINT [CK_TwoFactorAuth_HasFactor] CHECK ([SecretKey] IS NOT NULL OR [EmailCodeEnabledAt] IS NOT NULL)";
        table.Should().Contain(HasFactor);
        table.Replace("\r\n", "\n").TrimEnd().Should().EndWith(HasFactor + "\n);\nGO");
    }

    [Fact]
    public void AnEmailRow_HoldsASecondFactor_ButNoneThatCounts()
    {
        var row = EmailRow(Guid.NewGuid());

        row.HasSecondFactor.Should().BeTrue("a sign-in still asks for its second step");
        row.IsEnabled.Should().BeFalse("FA2 reads IsEnabled, so the policy sees no factor");
    }
}

/// <summary>
/// FA3: a recovery code counts as a second factor only when its set was issued with the
/// authenticator app or in a session that proved two factors.
/// </summary>
public class RecoveryCodeProvenanceTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    private async Task<SecondFactorProof> ProveRecoveryCodeAsync(bool qualified)
    {
        var totp = new Mock<ITotpService>();
        totp.Setup(t => t.VerifyRecoveryCode("ABCD-EFGH", "h1")).Returns(true);
        var row = EmailRow(_userId, recoveryCodes: "[\"h1\",\"h2\"]", recoveryCodesQualified: qualified);

        var proof = await new RecoveryCodeProofStrategy(totp.Object)
            .VerifyAsync(new SecondFactorReservation(row), "ABCD-EFGH", CancellationToken.None);

        return proof.Value;
    }

    [Fact]
    public async Task AnUnqualifiedCode_CompletesTheStep_ButRecordsNoSecondFactor()
    {
        var proof = await ProveRecoveryCodeAsync(qualified: false);

        proof.Methods.Should().Be(AuthenticationMethods.Unknown);
        AuthenticationMethods.Password.With(proof.Methods).IsMfaSatisfied.Should().BeFalse(
            "codes handed out after the password alone would otherwise turn one factor into two");
    }

    [Fact]
    public async Task AQualifiedCode_IsASecondFactor()
    {
        var proof = await ProveRecoveryCodeAsync(qualified: true);

        proof.Methods.Should().Be(AuthenticationMethods.RecoveryCode);
        AuthenticationMethods.Password.With(proof.Methods).IsMfaSatisfied.Should().BeTrue();
    }

    [Fact]
    public async Task SwitchingTheAuthenticatorOn_RenewingTheCodes_AndConfirmingAReplacement_EachQualifyTheirCodes()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => new { LastUsedTimeStep = (long?)null });
        var store = new TwoFactorStateStore(db);

        await store.TryEnableAsync(_userId, "v2:c", "[]", 100, true, null,
            new SessionUpgrade(Guid.NewGuid(), null, AuthenticationMethods.Totp), CancellationToken.None);
        await store.TryRegenerateCodesAsync(_userId, SecondFactorProof.Totp(100), true, "[]", "[\"n\"]", CancellationToken.None);
        await store.TryConfirmReplacementAsync(_userId, "v2:p", 100, "[]", "[\"n\"]", CancellationToken.None);

        db.Commands.Where(c => Sql(c).StartsWith("UPDATE dbo.TwoFactorAuth", StringComparison.Ordinal)
                               && Sql(c).Contains("RecoveryCodes = @", StringComparison.Ordinal))
            .Should().HaveCount(3)
            .And.OnlyContain(c => Sql(c).Contains("RecoveryCodesQualified = 1", StringComparison.Ordinal),
                "the column defaults to 0, so every writer of codes that count names it");
    }

    [Fact]
    public async Task AnEnabledAuthenticatorsCodes_AlwaysCount_WhateverTheColumnSays()
    {
        // A row the previous build enabled after the schema was published carries the
        // column's default 0; its codes were issued with the authenticator app all the same.
        var totp = new Mock<ITotpService>();
        totp.Setup(t => t.VerifyRecoveryCode("ABCD-EFGH", "h1")).Returns(true);
        var row = new TwoFactorSnapshot(_userId, "v2:c", "[\"h1\"]", isEnabled: true, failedAttempts: 0, lockedUntil: null,
            recoveryCodesQualified: false);

        var proof = await new RecoveryCodeProofStrategy(totp.Object)
            .VerifyAsync(new SecondFactorReservation(row), "ABCD-EFGH", CancellationToken.None);

        proof.Value.Methods.Should().Be(AuthenticationMethods.RecoveryCode);
    }

    [Fact]
    public void TheColumn_DefaultsToNotQualified_AndExistingAuthenticatorRowsAreQualifiedOnce()
    {
        var root = ApiSourceScan.SolutionDirectory();
        File.ReadAllText(Path.Combine(root, "Auth_DB", "dbo", "Tables", "Security", "TwoFactorAuth.sql"))
            .Should().Contain("[RecoveryCodesQualified] BIT NOT NULL CONSTRAINT [DF_TwoFactorAuth_RecoveryCodesQualified] DEFAULT 0");
        File.ReadAllText(Path.Combine(root, "Auth_DB", "dbo", "Scripts", "Upgrades", "2026-10-23_QualifyEnabledRecoveryCodes.sql"))
            .Should().Contain("SET [RecoveryCodesQualified] = 1").And.Contain("WHERE [IsEnabled] = 1");
        File.ReadAllText(Path.Combine(root, "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql"))
            .Should().Contain(@":r ..\Scripts\Upgrades\2026-10-23_QualifyEnabledRecoveryCodes.sql");
    }
}

/// <summary>
/// FA12: the dashboard's MFA number counts the factor policies accept, and the emailed
/// code apart; the account's page names each factor.
/// </summary>
public class UserStatsMfaSplitTests
{
    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    [Fact]
    public async Task TheMfaCount_ReadsTheFactorRow_NeverTheAccountFlag_AndCountsTheEmailedCodeApart()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, throwOn: _ => new OperationCanceledException("stop after recording"));

        var read = () => new DashboardStatsRepository(db).GetUserStatsAsync(30, "UTC", CancellationToken.None);
        await read.Should().ThrowAsync<OperationCanceledException>();

        var sql = Sql(db.Commands.Should().ContainSingle().Subject);
        sql.Should().Contain("ISNULL(SUM(CASE WHEN u.Status = 1 AND f.IsEnabled = 1 THEN 1 ELSE 0 END), 0) AS MfaEnabled");
        sql.Should().Contain("ISNULL(SUM(CASE WHEN u.Status = 1 AND f.EmailCodeEnabledAt IS NOT NULL THEN 1 ELSE 0 END), 0) AS EmailOnlyTwoFactor");
        sql.Should().Contain("FROM dbo.Users u LEFT JOIN dbo.TwoFactorAuth f ON f.UserId = u.Id WHERE u.IsDeleted = 0;");
        sql.Should().NotContain("IsTwoFactorEnabled", "the emailed code sets the account flag too, and counts for no policy");
    }

    [Fact]
    public async Task TheStatistics_CarryTheEmailedCodeApart()
    {
        var repository = new Mock<IDashboardStatsRepository>();
        repository.Setup(r => r.GetUserStatsAsync(30, "UTC", It.IsAny<CancellationToken>())).ReturnsAsync(new UserStatsSnapshot
        {
            TotalUsers = 10, ByStatus = [], MfaEnabled = 4, EmailOnlyTwoFactor = 3, ActiveUsers = 9, NewInWindow = 0,
            SignupsPerDay = [], CohortCreated = 0, CohortEmailConfirmed = 0, CohortLoggedIn = 0, DormantOver30Days = 0,
            DormantOver60Days = 0, DormantOver90Days = 0, NeverLoggedIn = 0, UsersByOrganization = [], TotalActiveMemberships = 0
        });

        var result = await new GetUserStatsQueryHandler(repository.Object, Mock.Of<ILogger<GetUserStatsQueryHandler>>())
            .Handle(new GetUserStatsQuery(30, "UTC"), CancellationToken.None);

        result.Value.MfaEnabled.Should().Be(4);
        result.Value.EmailOnlyTwoFactor.Should().Be(3);
    }

    [Theory]
    [InlineData("totp", true, true, false, true, false)]
    [InlineData("email", true, false, true, false, true)]
    [InlineData("none", false, false, false, false, true)]
    public async Task TheAccountsPage_NamesEachFactor(
        string factor, bool emailEnabled, bool totp, bool emailCode, bool qualifying, bool available)
    {
        var user = TestHelpers.CreateUser(twoFactorEnabled: factor != "none");
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var store = new Mock<ITwoFactorStateStore>();
        store.Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(factor switch
        {
            "totp" => TotpRow(user.Id),
            "email" => EmailRow(user.Id),
            _ => null
        });
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetUserRolesAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(p => p.GetUserEffectivePermissionsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var dto = (await new GetUserByIdQueryHandler(
                users.Object, roles.Object, permissions.Object, AccountRules.Policy(), Mock.Of<IImageUrlComposer>(), store.Object,
                TestHelpers.CreateOptions(new EmailSettings { Enabled = emailEnabled && available }))
            .Handle(new GetUserByIdQuery(user.Id), CancellationToken.None)).Value;

        dto.TotpEnabled.Should().Be(totp);
        dto.EmailCodeEnabled.Should().Be(emailCode);
        dto.QualifyingMfa.Should().Be(qualifying, "the emailed code is never a factor a policy accepts");
        dto.EmailCodeAvailable.Should().Be(emailEnabled && available);
    }

    // The API's own options (Program.cs, AddJsonOptions): camelCase, null omitted.
    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] FactorMembers = ["totpEnabled", "emailCodeEnabled", "qualifyingMfa", "emailCodeAvailable"];

    [Fact]
    public async Task TheListAndTheProfileUpdate_SayNothingAboutTheFactor_RatherThanNo()
    {
        // An authenticator account: "false" from either response would be a wrong answer.
        var user = TestHelpers.CreateUser(twoFactorEnabled: true);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetPagedAsync(1, 20, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([user], 1));
        users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetUserRolesAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        roles.Setup(r => r.GetUserRolesForUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<Role>> { [user.Id] = [] });
        var permissions = new Mock<IPermissionRepository>();
        permissions.Setup(p => p.GetUserEffectivePermissionsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        permissions.Setup(p => p.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<string>> { [user.Id] = [] });

        var listed =(await new GetUsersQueryHandler(users.Object, roles.Object, permissions.Object, AccountRules.Policy(), Mock.Of<IImageUrlComposer>())
            .Handle(new GetUsersQuery(), CancellationToken.None)).Value.Users.Single();
        var updated = (await new UpdateProfileCommandHandler(users.Object, Mock.Of<ILogger<UpdateProfileCommandHandler>>())
            .Handle(new UpdateProfileCommand(user.Id, FirstName: "Renamed"), CancellationToken.None)).Value;

        foreach (var dto in new[] { listed, updated })
        {
            new bool?[] { dto.TotpEnabled, dto.EmailCodeEnabled, dto.QualifyingMfa, dto.EmailCodeAvailable }
                .Should().AllSatisfy(member => member.Should().BeNull());
            var json = JsonSerializer.Serialize(dto, ApiJson);
            FactorMembers.Should().AllSatisfy(member => json.Should().NotContain($"\"{member}\""));
            json.Should().Contain("\"twoFactorEnabled\":true");
        }
    }
}
