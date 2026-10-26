using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The posture of the accounts that depend on email to sign in: those whose second factor
/// is the emailed code (S24, ARR-0694), and those that sign in only by emailed code
/// (OI-215, REVIEW 110 F8). A Warning while email sending is off and any do: the first can
/// then finish a sign-in only with a recovery code, and one with none left only through an
/// administrator's reset; the second cannot sign in at all, except through "forgot
/// password", which sets a first password by a link that email no longer sends either.
/// </summary>
/// <remarks>
/// Email:Enabled is hot and editable from the console, so this is evaluated after every
/// configuration reload as well as at startup. The counts are read in a scope of their
/// own, with the statements the console's confirmation reads, so the two never disagree.
/// </remarks>
public sealed class EmailCodeFactorPostureContributor : ISecurityPostureContributor
{
    private const string StrandedTemplate =
        "Email sending is off (Email:Enabled = false) while {Accounts} account(s) have the emailed code as their second factor: they can finish signing in only with a recovery code";
    private const string PasswordlessStrandedTemplate =
        "Email sending is off (Email:Enabled = false) while {Accounts} account(s) have no password and no linked provider: they sign in only with a code sent by email, so they cannot sign in";
    private const string InUseTemplate =
        "Emailed second factor: {Accounts} account(s); accounts signing in only by emailed code: {Passwordless}; email sending {EmailState}";

    private readonly IServiceScopeFactory _scopeFactory;

    public EmailCodeFactorPostureContributor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Name => "two-factor.email-code";

    /// <inheritdoc />
    public async Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var emailEnabled = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<EmailSettings>>().Value.Enabled;
        var accounts = await scope.ServiceProvider
            .GetRequiredService<ITwoFactorStateStore>()
            .CountEmailCodeFactorsAsync(cancellationToken);
        var passwordless = await scope.ServiceProvider
            .GetRequiredService<IUserRepository>()
            .CountEmailCodeOnlyAccountsAsync(cancellationToken);

        if (emailEnabled || (accounts == 0 && passwordless == 0))
        {
            return new SecurityPosture(
                SecurityPostureLevel.Ok,
                [],
                [new SecurityPostureLine(LogLevel.Information, InUseTemplate, [accounts, passwordless, emailEnabled ? "on" : "off"])],
                $"ok|{accounts}|{passwordless}|{emailEnabled}");
        }

        var lines = new List<SecurityPostureLine>();
        if (accounts > 0)
        {
            lines.Add(new SecurityPostureLine(LogLevel.Warning, StrandedTemplate, [accounts]));
        }

        if (passwordless > 0)
        {
            lines.Add(new SecurityPostureLine(LogLevel.Warning, PasswordlessStrandedTemplate, [passwordless]));
        }

        return new SecurityPosture(
            SecurityPostureLevel.Weak,
            ["two-factor.email-code: email sending is off"],
            lines,
            $"stranded|{accounts}|{passwordless}");
    }
}
