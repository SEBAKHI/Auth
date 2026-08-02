using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.SendTestEmail;

/// <summary>
/// Sends a diagnostic email to the calling administrator using the CURRENT
/// effective email settings, so a saved SMTP change can be verified without
/// waiting for a real flow to trigger a message.
/// </summary>
public record SendTestEmailCommand(Guid UserId) : IRequest<ErrorOr<Success>>;
