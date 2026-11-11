using System.Text;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GoogleClientSecret;

/// <summary>
/// Stores Google's OAuth client secret in the encrypted secrets file, where it becomes
/// <c>ExternalAuth:Google:ClientSecret</c>: the server's half of the Google sign-in, which
/// exchanges the one-time code Google sends back to the apps for the ID token.
/// </summary>
/// <param name="Value">The client secret, as Google Cloud Console shows it once.</param>
/// <param name="RequestedBy">The administrator performing the change.</param>
public record SetGoogleClientSecretCommand(
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
