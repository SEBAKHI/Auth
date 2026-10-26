using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.RegisterWithInvitation;

/// <summary>
/// Command to register a new account through an organization invitation.
/// The email comes from the invitation itself; possession of the emailed
/// single-use token proves mailbox ownership, so the account is created
/// with a confirmed email and the invitation is accepted in the same step.
/// </summary>
/// <remarks>
/// The password and the names are optional where the Registration switches leave
/// them out (OI-215); the handler decides, after the token was found.
/// </remarks>
public record RegisterWithInvitationCommand(
    string Token,
    string? Password,
    string? FirstName,
    string? LastName,
    string? PreferredLanguage = null,
    string? TimeZone = null) : IRequest<ErrorOr<RegisterWithInvitationResponse>>;
