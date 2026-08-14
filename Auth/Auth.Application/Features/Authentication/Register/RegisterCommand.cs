using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Register;

/// <summary>
/// Command to register a new user account with email and password.
/// Creates the user, optionally creates a personal organization, and sends email verification.
/// </summary>
public record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null,
    bool CreateOrganization = false) : IRequest<ErrorOr<RegisterResponse>>;
