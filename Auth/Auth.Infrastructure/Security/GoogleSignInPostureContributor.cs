using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Whether a Google sign-in can complete (S02 PR D). The apps offer Google on
/// <c>ExternalAuth:Google</c>'s <c>Enabled</c> and <c>ClientId</c> alone, but the sign-in also
/// needs the client secret (the code exchange) and at least one first-party app origin (the
/// exchange builds Google's callback address from it, and refuses every request while
/// <c>IdentityProvider:FirstPartySpaOrigins</c> is empty). With either missing, a person picks
/// their Google account and is then refused; an account that signs in only with Google cannot
/// sign in at all. A Warning while that holds, so the operator learns it at startup or at the
/// configuration reload that caused it, not from the first person who could not sign in.
/// </summary>
/// <remarks>
/// The secret is read from configuration as the exchange reads it: it reaches configuration
/// from the encrypted secrets file at startup, so a secret saved from the console counts once
/// the API restarted. The seeded <c>ExternalAuthProviders</c> row is a second switch this does
/// not read; with it off Google is not offered at all, and this line is only a notice.
/// </remarks>
public sealed class GoogleSignInPostureContributor : ISecurityPostureContributor
{
    private const string BrokenTemplate =
        "Google sign-in is on (ExternalAuth:Google) but cannot complete: {Missing}. A person who picks their Google account is refused, and an account that signs in only with Google cannot sign in";
    private const string ReadyTemplate =
        "Google sign-in is on, with its client secret and {Origins} first-party app origin(s)";
    private const string OffTemplate = "Google sign-in is off";

    private readonly IServiceScopeFactory _scopeFactory;

    public GoogleSignInPostureContributor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Name => "external-auth.google";

    /// <inheritdoc />
    public Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var google = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ExternalAuthSettings>>().Value.Google;
        var origins = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityProviderSettings>>().Value
            .FirstPartySpaOrigins.Count(origin => !string.IsNullOrWhiteSpace(origin));

        if (google is null || !google.Enabled || string.IsNullOrWhiteSpace(google.ClientId))
        {
            return Task.FromResult(new SecurityPosture(
                SecurityPostureLevel.Ok,
                [],
                [new SecurityPostureLine(LogLevel.Information, OffTemplate, [])],
                "off"));
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(google.ClientSecret))
        {
            missing.Add("no client secret (ExternalAuth:Google:ClientSecret; set it on the console's Secrets page, then restart the API)");
        }

        if (origins == 0)
        {
            missing.Add("no first-party app origin (IdentityProvider:FirstPartySpaOrigins is empty)");
        }

        return Task.FromResult(missing.Count > 0
            ? new SecurityPosture(
                SecurityPostureLevel.Weak,
                ["external-auth.google.incomplete"],
                [new SecurityPostureLine(LogLevel.Warning, BrokenTemplate, [string.Join("; ", missing)])],
                "incomplete:" + string.Join("|", missing))
            : new SecurityPosture(
                SecurityPostureLevel.Ok,
                [],
                [new SecurityPostureLine(LogLevel.Information, ReadyTemplate, [origins])],
                $"ready:{origins}"));
    }
}
