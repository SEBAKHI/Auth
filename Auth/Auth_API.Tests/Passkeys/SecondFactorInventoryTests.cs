using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (FA2): the one definition of "the account has a qualifying second factor" every MFA
/// policy reads — the authenticator app, or a live, user-verified passkey of the RP ID in
/// force. It ignores <c>Passkeys:Enabled</c> and the posture. Without an RP ID or the schema it
/// is today's text, naming no passkey table.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>[RevokedAt] IS NULL</c> dropped from the passkey
/// fragment; <c>UserPasskeys</c> named in the statements used without the schema; a
/// <c>PasskeySettings</c> dependency added to the store; the schema registration dropped.
/// </remarks>
public class SecondFactorInventoryTests
{
    private const string TotpFragment =
        "SELECT 1 FROM [dbo].[TwoFactorAuth] WHERE [UserId] = {0} AND [IsEnabled] = 1)";

    private const string PasskeyFragment =
        "SELECT 1 FROM [dbo].[UserPasskeys] WHERE [UserId] = {0} AND [RevokedAt] IS NULL AND [IsUserVerified] = 1 AND [RpId] = @RpId)";

    private static readonly Guid UserId = Guid.NewGuid();

    private static TwoFactorStateStore Store(RecordingDbConnectionFactory db, string rpId = PasskeyTestDoubles.RpId, bool schema = true) =>
        new(db, NullLogger<TwoFactorStateStore>.Instance, new PasskeyRpIdAtStartup(rpId), PasskeyTestDoubles.Schema(schema));

    [Fact]
    public async Task HasEnabledFactor_WithAnRpIdAndTheSchema_IsTheAppOrALiveVerifiedPasskeyOfThatRpId()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        (await Store(db).HasEnabledFactorAsync(UserId, CancellationToken.None)).Should().BeTrue();

        var command = db.Commands.Should().ContainSingle().Subject;
        var text = Squash(command.CommandText);
        text.Should().Contain(Squash(string.Format(TotpFragment, "@UserId")))
            .And.Contain(Squash(string.Format(PasskeyFragment, "@UserId")))
            .And.Contain(") OR EXISTS (");
        command.Parameters["RpId"].Should().Be(PasskeyTestDoubles.RpId, "a passkey of another RP ID never counts (PK3)");
        command.Parameters["UserId"].Should().Be(UserId);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(PasskeyTestDoubles.RpId, false)]
    public async Task HasEnabledFactor_WithoutAnRpIdOrTheSchema_IsTheAppAlone_NamingNoPasskeyTable(string rpId, bool schema)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        (await Store(db, rpId, schema).HasEnabledFactorAsync(UserId, CancellationToken.None)).Should().BeFalse();

        var text = db.Commands.Single().CommandText;
        text.Should().NotContain("UserPasskeys", "a statement naming a missing table fails when it is bound");
        text.Should().Be(TwoFactorStateStore.WithoutPasskeys.HasEnabledFactor);
        Squash(text).Should().Contain(Squash(string.Format(TotpFragment, "@UserId")));
    }

    /// <summary>
    /// REVIEW 150 F6: without an RP ID or the passkey schema the store sends exactly what main
    /// sent before S20 — compared with a golden copy generated from main's own source, not with
    /// the variant itself — so every account keeps the answers it had.
    /// </summary>
    /// <remarks>Deliberate break, verified red: one predicate of the variant changed.</remarks>
    [Theory]
    [InlineData("HasEnabledFactor")]
    [InlineData("ApplicationMfaReadiness")]
    [InlineData("HasPlatformRoleHolderWithoutFactor")]
    public void WithoutPasskeys_IsMainsTextBeforeS20(string statement)
    {
        var golden = File.ReadAllText(Path.Combine(
                ApiSourceScan.SolutionDirectory(), "Auth_API.Tests", "Passkeys", "FactorStatementsBeforeS20.golden.sql"))
            .Replace("\r\n", "\n");
        var start = golden.IndexOf($"-- @@ {statement}\n", StringComparison.Ordinal);
        start.Should().BePositive($"the golden file holds {statement}");
        start += $"-- @@ {statement}\n".Length;
        var end = golden.IndexOf("\n-- @@ ", start, StringComparison.Ordinal);
        var mains = golden[start..(end < 0 ? golden.Length : end)];

        var sent = statement switch
        {
            "HasEnabledFactor" => TwoFactorStateStore.WithoutPasskeys.HasEnabledFactor,
            "ApplicationMfaReadiness" => TwoFactorStateStore.WithoutPasskeys.ApplicationMfaReadiness,
            _ => TwoFactorStateStore.WithoutPasskeys.HasPlatformRoleHolderWithoutFactor,
        };

        Squash(sent).Should().Be(Squash(mains), $"{statement} without passkeys is main's statement before S20");
    }

    [Fact]
    public async Task QualifyingFactors_AreReadApart_WithTheSameFragments()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new { Totp = false, Passkey = true });

        var factors = await Store(db).GetQualifyingFactorsAsync(UserId, CancellationToken.None);

        factors.Totp.Should().BeFalse();
        factors.Passkey.Should().BeTrue();
        factors.IsPasskeyOnly.Should().BeTrue();
        var text = Squash(db.Commands.Single().CommandText);
        text.Should().Contain(Squash(string.Format(TotpFragment, "@UserId")) + " THEN 1 ELSE 0 END AS BIT) AS [Totp]")
            .And.Contain(Squash(string.Format(PasskeyFragment, "@UserId")) + " THEN 1 ELSE 0 END AS BIT) AS [Passkey]");
    }

    [Fact]
    public void WithoutPasskeys_EveryStatementIsTheAppAlone()
    {
        var statements = TwoFactorStateStore.WithoutPasskeys;
        string[] all =
        [
            statements.HasEnabledFactor, statements.QualifyingFactors,
            statements.ApplicationMfaReadiness, statements.HasPlatformRoleHolderWithoutFactor,
        ];

        all.Should().HaveCount(4).And.OnlyContain(text => !text.Contains("UserPasskeys"));
        Squash(statements.QualifyingFactors).Should().Contain("CAST(0 AS BIT) AS [Passkey]");
    }

    /// <summary>
    /// The readiness count of S23 and the platform-role check read the same definition, each
    /// filled with its own user column.
    /// </summary>
    [Theory]
    [InlineData(nameof(TwoFactorStateStore.FactorStatements.HasEnabledFactor), "@UserId")]
    [InlineData(nameof(TwoFactorStateStore.FactorStatements.ApplicationMfaReadiness), "a.[UserId]")]
    [InlineData(nameof(TwoFactorStateStore.FactorStatements.HasPlatformRoleHolderWithoutFactor), "ur.[UserId]")]
    public void WithPasskeys_EveryFa2Statement_ReadsTheAppOrAPasskey(string statement, string userColumn)
    {
        var text = (string)typeof(TwoFactorStateStore.FactorStatements).GetProperty(statement)!
            .GetValue(TwoFactorStateStore.WithPasskeys)!;

        Squash(text).Should().Contain(
            "(EXISTS ( " + Squash(string.Format(TotpFragment, userColumn)) + " OR EXISTS ( " + Squash(string.Format(PasskeyFragment, userColumn)) + ")");
    }

    [Fact]
    public void TheDefinition_IgnoresTheFeatureSwitch()
    {
        // Switching passkeys off must not turn an administrator whose only factor is a passkey
        // into one with none: the store has no way to read the switch.
        var parameters = typeof(TwoFactorStateStore).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        parameters.Should().NotContain(typeof(IOptions<PasskeySettings>))
            .And.NotContain(typeof(IOptionsMonitor<PasskeySettings>))
            .And.NotContain(typeof(IOptionsSnapshot<PasskeySettings>))
            .And.NotContain(typeof(Auth.Application.Features.Passkeys.Common.PasskeyFeature));
        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "TwoFactorStateStore.cs"))
            .Should().NotContain("PasskeySettings");
    }

    [Fact]
    public void TheEmailedCode_NeverCounts_ItsRowIsNeverEnabled()
    {
        // The app fragment reads IsEnabled = 1, and an emailed-code factor's row cannot have it.
        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "TwoFactorAuth.sql"))
            .Should().Contain("CK_TwoFactorAuth_SingleFactor");
    }

    /// <summary>
    /// The store takes the RP ID and the schema as optional parameters, so a missing
    /// registration would silently drop passkeys from FA2 — and with it the rule that a
    /// further factor needs a two-factor session. The API registers both, and the container
    /// hands them over.
    /// </summary>
    [Fact]
    public async Task TheApiContainer_GivesTheStoreTheRpIdAndTheSchema()
    {
        var program = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "Program.cs"));
        program.Should().Contain("builder.Services.AddSingleton(PasskeyRpIdAtStartup.From(builder.Configuration));")
            .And.Contain("builder.Services.AddSingleton<IPasskeySchema, PasskeySchemaProbe>();")
            .And.Contain("builder.Services.AddScoped<ITwoFactorStateStore, TwoFactorStateStore>();");

        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDbConnectionFactory>(db)
            .AddSingleton(new PasskeyRpIdAtStartup(PasskeyTestDoubles.RpId))
            .AddSingleton(PasskeyTestDoubles.Schema())
            .AddScoped<ITwoFactorStateStore, TwoFactorStateStore>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ITwoFactorStateStore>().HasEnabledFactorAsync(UserId, CancellationToken.None);

        db.Commands.Single().CommandText.Should().Contain("UserPasskeys", "the container filled both optional parameters");
    }

    private static string Squash(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
}
