using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Command to authenticate a user via an external provider (Google, Apple, etc.).
/// If the user doesn't exist, creates a new account. If they do, logs them in.
/// </summary>
public record ExternalLoginCommand(
    string Provider,
    string IdToken,
    string? Nonce,
    bool CreateOrganization = false,
    string? IpAddress = null,
    string? UserAgent = null,
    string? DeviceId = null) : IRequest<ErrorOr<LoginResponse>>;
