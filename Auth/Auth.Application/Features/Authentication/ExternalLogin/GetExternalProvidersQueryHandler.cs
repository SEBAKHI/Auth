using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Handler for retrieving usable external authentication providers.
/// <para>
/// "Usable" means all three of: the directory row is enabled, the configuration
/// section is enabled, and a public client id is configured. A provider that
/// fails any of them cannot complete a sign-in — the token it produced would be
/// rejected by audience validation — so listing it would only render a button
/// that always fails. This is also what makes the console's per-provider toggle
/// and client id real for the sign-in UI: it reads them from here at runtime
/// instead of from a build-time constant baked into the SPA.
/// </para>
/// </summary>
public class GetExternalProvidersQueryHandler
    : IRequestHandler<GetExternalProvidersQuery, ErrorOr<IReadOnlyList<ExternalAuthProviderResponse>>>
{
    private readonly IExternalAuthProviderRepository _providerRepository;

    // Snapshot, not IOptions: the console can change either field, and the next
    // request must see it.
    private readonly IOptionsSnapshot<ExternalAuthSettings> _externalAuthSettings;

    public GetExternalProvidersQueryHandler(
        IExternalAuthProviderRepository providerRepository,
        IOptionsSnapshot<ExternalAuthSettings> externalAuthSettings)
    {
        _providerRepository = providerRepository;
        _externalAuthSettings = externalAuthSettings;
    }

    public async Task<ErrorOr<IReadOnlyList<ExternalAuthProviderResponse>>> Handle(
        GetExternalProvidersQuery request,
        CancellationToken cancellationToken)
    {
        var providers = await _providerRepository.GetAllEnabledAsync(
            request.SortBy, request.SortDirection, cancellationToken);

        var settings = _externalAuthSettings.Value;

        var response = providers
            .Select(p => (Provider: p, Configured: ResolveConfiguration(settings, p.Code)))
            .Where(x => x.Configured is { Enabled: true, ClientId.Length: > 0 })
            .Select(x => new ExternalAuthProviderResponse(
                x.Provider.Code,
                x.Provider.Name,
                x.Provider.IconUrl,
                x.Configured!.Value.ClientId))
            .ToList();

        return response;
    }

    /// <summary>
    /// Maps a directory row's code onto its configuration section. An unknown
    /// code has no configuration and therefore no usable client id.
    /// </summary>
    private static (bool Enabled, string ClientId)? ResolveConfiguration(
        ExternalAuthSettings settings,
        string code) => code.ToLowerInvariant() switch
        {
            "google" when settings.Google is { } google => (google.Enabled, google.ClientId ?? string.Empty),
            "apple" when settings.Apple is { } apple => (apple.Enabled, apple.ServicesId ?? string.Empty),
            _ => null
        };
}
