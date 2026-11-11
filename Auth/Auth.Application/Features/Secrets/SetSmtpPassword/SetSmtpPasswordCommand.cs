using System.Text;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.SetSmtpPassword;

/// <summary>
/// Command to store the SMTP password in the encrypted secrets file, where it
/// overrides <c>Email:Password</c> from configuration.
/// </summary>
/// <param name="Value">The SMTP password.</param>
/// <param name="RequestedBy">The administrator performing the change.</param>
public record SetSmtpPasswordCommand(
    string Value,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>
{
    /// <summary>Names the password without its value, so no log line can carry it.</summary>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Value = [redacted], RequestedBy = ").Append(RequestedBy);
        return true;
    }
}
