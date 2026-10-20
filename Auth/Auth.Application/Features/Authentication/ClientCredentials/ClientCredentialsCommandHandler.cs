using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.ClientCredentials;

/// <summary>
/// Issues a machine access token to an application that proves who it is with its machine key
/// (OI-128).
/// </summary>
/// <remarks>
/// <para>
/// Every refusal of the client is the one published answer <c>Auth.InvalidClient</c>: an unknown
/// or revoked key, an inactive or deleted application, a bad signature, any assertion rule, a
/// replay. Which check failed goes to the log only, so the answer is no oracle for an attacker
/// probing the rules one by one.
/// </para>
/// <para>
/// The order matters. The client is verified by the token endpoint's one client authenticator
/// (<see cref="IClientAuthenticator"/>, OI-171), with a key of purpose
/// <see cref="MachineKeyPurpose.Machine"/>: a sign-in key of a server-side web application is
/// refused here. The scope is checked after the client authenticated, so only a client learns
/// what the scope list holds, and before the jti is spent, so a refused scope spends nothing.
/// </para>
/// <para>
/// The endpoint is anonymous. What binds a call to an application is the signed assertion and its
/// single-use jti; a server-to-server call has no browser, so no Origin check applies. The
/// token endpoint's <c>token</c> rate-limit policy, at the API and at the gateway, covers it.
/// </para>
/// </remarks>
public class ClientCredentialsCommandHandler
    : IRequestHandler<ClientCredentialsCommand, ErrorOr<ClientCredentialsTokenResponse>>
{
    /// <summary>RFC 7523 §2.2: the one client assertion type.</summary>
    public const string JwtBearerAssertionType = ClientAuthenticator.JwtBearerAssertionType;

    private readonly IClientAuthenticator _authenticator;
    private readonly IApplicationMachineKeyRepository _machineKeys;
    private readonly IApplicationRepository _applications;
    private readonly IJwtTokenService _tokens;
    private readonly ILogger<ClientCredentialsCommandHandler> _logger;

    public ClientCredentialsCommandHandler(
        IClientAuthenticator authenticator,
        IApplicationMachineKeyRepository machineKeys,
        IApplicationRepository applications,
        IJwtTokenService tokens,
        ILogger<ClientCredentialsCommandHandler> logger)
    {
        _authenticator = authenticator;
        _machineKeys = machineKeys;
        _applications = applications;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<ErrorOr<ClientCredentialsTokenResponse>> Handle(
        ClientCredentialsCommand request,
        CancellationToken cancellationToken)
    {
        var verified = await _authenticator.VerifyAsync(
            new ClientAssertionRequest(request.ClientAssertionType, request.ClientAssertion, request.ClientId, request.IpAddress),
            MachineKeyPurpose.Machine,
            cancellationToken);
        if (verified.IsError)
        {
            return verified.Errors;
        }

        var client = verified.Value;
        var clientId = client.ApplicationCode;
        if (Guid.TryParse(clientId, out _))
        {
            // The token's sub is the application code, and several readers take a GUID-shaped
            // sub for a user id: an application whose code is a user's id would act as that user.
            // The code rule admits such a code, so the grant refuses to mint for one. A rule of
            // this grant alone: the user grants' sub is the user.
            return Refuse(request, "application code is GUID-shaped", client.Kid);
        }

        var scopes = GrantedScopes(request.Scope, await ConsentedScopesAsync(request.Scope, client, cancellationToken));
        if (scopes is null)
        {
            _logger.LogWarning(
                "client_credentials refused an unknown scope for application {ApplicationId}, key {Kid}",
                client.ApplicationId, client.Kid);
            return AuthErrors.InvalidScope;
        }

        var spent = await _authenticator.SpendAsync(client, request.IpAddress, cancellationToken);
        if (spent.IsError)
        {
            return spent.Errors;
        }

        var accessToken = _tokens.GenerateMachineAccessToken(clientId, scopes);
        await _machineKeys.RecordUsageAsync(client.KeyId, cancellationToken);

        _logger.LogInformation(
            "Machine token issued to application {ApplicationId} with key {Kid}",
            client.ApplicationId, client.Kid);

        return new ClientCredentialsTokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = (int)MachineClients.AccessTokenLifetime.TotalSeconds,
            Scope = string.Join(' ', scopes)
        };
    }

    /// <summary>
    /// The scopes to grant: every default machine scope (<see cref="MachineClients.Scopes.All"/>)
    /// when none is asked for, otherwise exactly those asked for, in the catalogue's order; null
    /// when any asked-for value is neither a default machine scope nor one of
    /// <paramref name="consented"/>. A consent-only scope is never granted unasked (OI-202), and
    /// one the application was not consented is refused exactly as an unknown name.
    /// </summary>
    internal static IReadOnlyList<string>? GrantedScopes(string? scope, IReadOnlyCollection<string> consented)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return MachineClients.Scopes.All;
        }

        IReadOnlyList<string> grantable =
            [.. MachineClients.Scopes.All, .. MachineClients.Scopes.ConsentOnly.Where(name => consented.Contains(name, StringComparer.Ordinal))];
        var requested = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (!requested.All(value => grantable.Contains(value, StringComparer.Ordinal)))
        {
            return null;
        }

        return grantable.Where(requested.Contains).ToList();
    }

    /// <summary>
    /// The consent-only scopes the client's application is consented now, read only when the
    /// request names one of them: a request for the default scopes costs no read.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> ConsentedScopesAsync(
        string? scope,
        AuthenticatedClient client,
        CancellationToken cancellationToken)
    {
        if (!MachineClients.Scopes.ConsentOnly.Any(name => MachineClients.Scopes.Grants(scope, name)))
        {
            return [];
        }

        var application = await _applications.GetByIdAsync(client.ApplicationId, cancellationToken);
        return application?.ConsentedMachineScopes ?? [];
    }

    private Error Refuse(ClientCredentialsCommand request, string reason, string? kid = null)
    {
        _logger.LogWarning(
            "client_credentials refused: {Reason}. Key {Kid}. IP: {IpAddress}",
            reason, kid, request.IpAddress);
        return AuthErrors.InvalidClient;
    }
}
