using System.Text;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.TurnstileSecret;

/// <summary>
/// Stores Cloudflare Turnstile's secret key in the encrypted secrets file (S13), where it becomes
/// <c>BotDefense:TurnstileSecret</c>: the server's half of the bot check, with which it asks
/// Cloudflare whether a token is good. Takes effect on the next API restart.
/// </summary>
/// <param name="Value">The secret key, as Cloudflare's dashboard shows it.</param>
/// <param name="RequestedBy">The administrator performing the change.</param>
public record SetTurnstileSecretCommand(
    string Value,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>
{
    /// <summary>Names the secret without its value, so no log line can carry it.</summary>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Value = [redacted], RequestedBy = ").Append(RequestedBy);
        return true;
    }
}

/// <summary>
/// Removes Cloudflare Turnstile's secret key from the encrypted secrets file (S13). After the next
/// API restart the bot check has no secret: with a sitekey still set it is Misconfigured and the
/// fail mode decides; with neither, it is not in place. An owner's way out that needs no sqlcmd.
/// </summary>
/// <param name="RequestedBy">The administrator performing the change.</param>
public record ClearTurnstileSecretCommand(Guid RequestedBy) : IRequest<ErrorOr<Success>>;
