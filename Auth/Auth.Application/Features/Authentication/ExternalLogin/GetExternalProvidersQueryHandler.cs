using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Handler for retrieving enabled external authentication providers.
/// </summary>
public class GetExternalProvidersQueryHandler
    : IRequestHandler<GetExternalProvidersQuery, ErrorOr<IReadOnlyList<ExternalAuthProviderResponse>>>
{
    private readonly IExternalAuthProviderRepository _providerRepository;

    public GetExternalProvidersQueryHandler(IExternalAuthProviderRepository providerRepository)
    {
        _providerRepository = providerRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<ExternalAuthProviderResponse>>> Handle(
        GetExternalProvidersQuery request,
        CancellationToken cancellationToken)
    {
        var providers = await _providerRepository.GetAllEnabledAsync(
            request.SortBy, request.SortDirection, cancellationToken);

        var response = providers
            .Select(p => new ExternalAuthProviderResponse(p.Code, p.Name, p.IconUrl))
            .ToList();

        return response;
    }
}
