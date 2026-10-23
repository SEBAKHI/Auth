using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The emailed second factor's posture (S24, ARR-0694): how many accounts depend on it,
/// and a Warning while email sending is off and any do — each of them can then finish a
/// sign-in only with a recovery code, and one with none left only through an
/// administrator's reset.
/// </summary>
/// <remarks>
/// Email:Enabled is hot and editable from the console, so this is evaluated after every
/// configuration reload as well as at startup. The count is read in a scope of its own.
/// </remarks>
public sealed class EmailCodeFactorPostureContributor : ISecurityPostureContributor
{
    private const string StrandedTemplate =
        "Email sending is off (Email:Enabled = false) while {Accounts} account(s) have the emailed code as their second factor: they can finish signing in only with a recovery code";
    private const string InUseTemplate =
        "Emailed second factor: {Accounts} account(s), email sending {EmailState}";

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

        return !emailEnabled && accounts > 0
            ? new SecurityPosture(
                SecurityPostureLevel.Weak,
                ["two-factor.email-code: email sending is off"],
                [new SecurityPostureLine(LogLevel.Warning, StrandedTemplate, [accounts])],
                $"stranded|{accounts}")
            : new SecurityPosture(
                SecurityPostureLevel.Ok,
                [],
                [new SecurityPostureLine(LogLevel.Information, InUseTemplate, [accounts, emailEnabled ? "on" : "off"])],
                $"ok|{accounts}|{emailEnabled}");
    }
}
