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
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
