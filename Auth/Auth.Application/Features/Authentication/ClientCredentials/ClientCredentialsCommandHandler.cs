using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// The order matters. The jti is recorded only after the signature verified: written before it,
/// an anonymous caller could fill the table with identifiers of its choosing, and pre-spend an
/// application's next ones. And the scope is checked after the client authenticated, so only a
/// client learns what the scope list holds.
/// </para>
/// <para>
/// The endpoint is anonymous. What binds a call to an application is the signed assertion and its
/// single-use jti; a server-to-server call has no browser, so no Origin check applies. The
/// token endpoint's <c>login</c> rate-limit policy and the gateway's <c>auth</c> policy cover it.
/// </para>
/// </remarks>
public class ClientCredentialsCommandHandler
    : IRequestHandler<ClientCredentialsCommand, ErrorOr<ClientCredentialsTokenResponse>>
{
    /// <summary>RFC 7523 §2.2: the one client assertion type.</summary>
    public const string JwtBearerAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly IApplicationMachineKeyRepository _machineKeys;
    private readonly IClientAssertionJtiStore _jtis;
    private readonly IClientAssertionVerifier _verifier;
    private readonly IJwtTokenService _tokens;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<ClientCredentialsCommandHandler> _logger;

    public ClientCredentialsCommandHandler(
        IApplicationMachineKeyRepository machineKeys,
        IClientAssertionJtiStore jtis,
        IClientAssertionVerifier verifier,
        IJwtTokenService tokens,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<ClientCredentialsCommandHandler> logger)
    {
        _machineKeys = machineKeys;
        _jtis = jtis;
        _verifier = verifier;
        _tokens = tokens;
        // The same source as the discovery document's "issuer", which is the audience a client
        // is told to address its assertion to.
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<ClientCredentialsTokenResponse>> Handle(
        ClientCredentialsCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ClientAssertionType, JwtBearerAssertionType, StringComparison.Ordinal)
            || string.IsNullOrEmpty(request.ClientAssertion))
        {
            return Refuse(request, "no jwt-bearer client assertion");
        }

        var kid = _verifier.ReadKeyId(request.ClientAssertion);
        if (kid is null)
        {
            return Refuse(request, "assertion unreadable or without kid");
        }

        var credential = await _machineKeys.GetActiveByKidAsync(kid, cancellationToken);
        if (credential is null)
        {
            return Refuse(request, "no active key of a live application for the kid", kid);
        }

        var clientId = credential.ApplicationCode;
        if (request.ClientId is not null && !string.Equals(request.ClientId, clientId, StringComparison.Ordinal))
        {
            return Refuse(request, "client_id is not the key's application", kid);
        }

        var verification = await _verifier.VerifyAsync(
            request.ClientAssertion,
            credential.Key.PublicKeyJwk,
            new ClientAssertionRules(clientId, _jwtSettings.Issuer, _jwtSettings.ClockSkew));
        if (!verification.IsValid)
        {
            return Refuse(request, verification.FailureReason ?? "assertion refused", kid);
        }

        var scopes = GrantedScopes(request.Scope);
        if (scopes is null)
        {
            _logger.LogWarning(
                "client_credentials refused an unknown scope for application {ApplicationId}, key {Kid}",
                credential.Key.ApplicationId, kid);
            return AuthErrors.InvalidScope;
        }

        var firstUse = await _jtis.TryConsumeAsync(
            credential.Key.ApplicationId, verification.Jti!, verification.ExpiresAtUtc!.Value, cancellationToken);
        if (!firstUse)
        {
            return Refuse(request, "assertion replayed (jti already used)", kid);
        }

        var accessToken = _tokens.GenerateMachineAccessToken(clientId, scopes);
        await _machineKeys.RecordUsageAsync(credential.Key.Id, cancellationToken);

        _logger.LogInformation(
            "Machine token issued to application {ApplicationId} with key {Kid}",
            credential.Key.ApplicationId, kid);

        return new ClientCredentialsTokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = (int)MachineClients.AccessTokenLifetime.TotalSeconds,
            Scope = string.Join(' ', scopes)
        };
    }

    /// <summary>
    /// The scopes to grant: every machine scope when none is asked for, otherwise exactly those
    /// asked for, in the catalogue's order; null when any asked-for value is not a machine scope.
    /// </summary>
    internal static IReadOnlyList<string>? GrantedScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return MachineClients.Scopes.All;
        }

        var requested = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (!requested.All(value => MachineClients.Scopes.All.Contains(value, StringComparer.Ordinal)))
        {
            return null;
        }

        return MachineClients.Scopes.All.Where(requested.Contains).ToList();
    }

    private Error Refuse(ClientCredentialsCommand request, string reason, string? kid = null)
    {
        _logger.LogWarning(
            "client_credentials refused: {Reason}. Key {Kid}. IP: {IpAddress}",
            reason, kid, request.IpAddress);
        return AuthErrors.InvalidClient;
    }
}
