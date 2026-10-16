using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The token endpoint's <c>private_key_jwt</c> client authentication (OI-128, extracted for every
/// grant by OI-171). See <see cref="IClientAuthenticator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The jti is recorded only after the signature verified: written before it, an anonymous caller
/// could fill the table with identifiers of its choosing, and pre-spend an application's next
/// ones.
/// </para>
/// <para>
/// A key authenticates only the grants of its purpose (owner decision D-171-2 (a)). A sign-in key
/// sits on a server-side web application, which faces the internet; refusing it at
/// <c>client_credentials</c> keeps a leak of that server from becoming a machine credential, and
/// refusing a machine key at the user grants keeps a batch job's key from redeeming a person's
/// sign-in.
/// </para>
/// </remarks>
public sealed class ClientAuthenticator : IClientAuthenticator
{
    /// <summary>RFC 7523 §2.2: the one client assertion type.</summary>
    public const string JwtBearerAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly IApplicationMachineKeyRepository _keys;
    private readonly IClientAssertionJtiStore _jtis;
    private readonly IClientAssertionVerifier _verifier;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<ClientAuthenticator> _logger;

    public ClientAuthenticator(
        IApplicationMachineKeyRepository keys,
        IClientAssertionJtiStore jtis,
        IClientAssertionVerifier verifier,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<ClientAuthenticator> logger)
    {
        _keys = keys;
        _jtis = jtis;
        _verifier = verifier;
        // The same source as the discovery document's "issuer", which is the audience a client
        // is told to address its assertion to.
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<AuthenticatedClient>> VerifyAsync(
        ClientAssertionRequest request,
        MachineKeyPurpose requiredPurpose,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.AssertionType, JwtBearerAssertionType, StringComparison.Ordinal)
            || string.IsNullOrEmpty(request.Assertion))
        {
            return Refuse(request, requiredPurpose, "no jwt-bearer client assertion");
        }

        var kid = _verifier.ReadKeyId(request.Assertion);
        if (kid is null)
        {
            return Refuse(request, requiredPurpose, "assertion unreadable or without kid");
        }

        var credential = await _keys.GetActiveByKidAsync(kid, cancellationToken);
        if (credential is null)
        {
            return Refuse(request, requiredPurpose, "no active key of a live application for the kid", kid);
        }

        if (credential.Key.Purpose != requiredPurpose)
        {
            return Refuse(request, requiredPurpose, $"the key's purpose is {credential.Key.Purpose}", kid);
        }

        var clientId = credential.ApplicationCode;
        if (request.ClientId is not null && !string.Equals(request.ClientId, clientId, StringComparison.Ordinal))
        {
            return Refuse(request, requiredPurpose, "client_id is not the key's application", kid);
        }

        var verification = await _verifier.VerifyAsync(
            request.Assertion,
            credential.Key.PublicKeyJwk,
            new ClientAssertionRules(clientId, _jwtSettings.Issuer, _jwtSettings.ClockSkew));
        if (!verification.IsValid)
        {
            return Refuse(request, requiredPurpose, verification.FailureReason ?? "assertion refused", kid);
        }

        return new AuthenticatedClient(
            credential.Key.Id,
            kid,
            credential.Key.ApplicationId,
            clientId,
            verification.Jti!,
            verification.ExpiresAtUtc!.Value);
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> SpendAsync(
        AuthenticatedClient client,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var firstUse = await _jtis.TryConsumeAsync(
            client.ApplicationId, client.Jti, client.ExpiresAtUtc, cancellationToken);
        if (!firstUse)
        {
            _logger.LogWarning(
                "Client authentication refused: assertion replayed (jti already used). Key {Kid}. IP: {IpAddress}",
                client.Kid, ipAddress);
            return AuthErrors.InvalidClient;
        }

        return Result.Success;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<AuthenticatedClient>> AuthenticateAsync(
        ClientAssertionRequest request,
        MachineKeyPurpose requiredPurpose,
        CancellationToken cancellationToken)
    {
        var verified = await VerifyAsync(request, requiredPurpose, cancellationToken);
        if (verified.IsError)
        {
            return verified.Errors;
        }

        var spent = await SpendAsync(verified.Value, request.IpAddress, cancellationToken);
        if (spent.IsError)
        {
            return spent.Errors;
        }

        await _keys.RecordUsageAsync(verified.Value.KeyId, cancellationToken);
        return verified.Value;
    }

    private Error Refuse(ClientAssertionRequest request, MachineKeyPurpose requiredPurpose, string reason, string? kid = null)
    {
        _logger.LogWarning(
            "Client authentication for a {Purpose} grant refused: {Reason}. Key {Kid}. IP: {IpAddress}",
            requiredPurpose, reason, kid, request.IpAddress);
        return AuthErrors.InvalidClient;
    }
}
