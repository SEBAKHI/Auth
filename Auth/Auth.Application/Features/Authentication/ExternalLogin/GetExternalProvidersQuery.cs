using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Query to get all enabled external authentication providers for UI rendering.
/// </summary>
public record GetExternalProvidersQuery() : IRequest<ErrorOr<IReadOnlyList<ExternalAuthProviderResponse>>>;
