using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Query to get all enabled external authentication providers for UI rendering.
/// </summary>
public record GetExternalProvidersQuery(
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<ExternalAuthProviderResponse>>>;
