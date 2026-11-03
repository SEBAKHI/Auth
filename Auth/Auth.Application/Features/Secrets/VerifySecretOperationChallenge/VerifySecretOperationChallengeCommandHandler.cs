using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Security.Keyring;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.VerifySecretOperationChallenge;

/// <summary>
/// Handler for answering a step-up confirmation. On a correct code it opens the
/// approval window and costs the operation, so the administrator's final
/// decision is made against live figures rather than a generic warning.
/// </summary>
public class VerifySecretOperationChallengeCommandHandler
    : IRequestHandler<VerifySecretOperationChallengeCommand, ErrorOr<SecretRotationImpactDto>>
{
    private readonly SecretOperationChallengeService _challengeService;
    private readonly KeyringApprovals _keyrings;
    private readonly ISecretRotationImpactRepository _impactRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<VerifySecretOperationChallengeCommandHandler> _logger;

    public VerifySecretOperationChallengeCommandHandler(
        SecretOperationChallengeService challengeService,
        KeyringApprovals keyrings,
        ISecretRotationImpactRepository impactRepository,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<VerifySecretOperationChallengeCommandHandler> logger)
    {
        _challengeService = challengeService;
        _keyrings = keyrings;
        _impactRepository = impactRepository;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<SecretRotationImpactDto>> Handle(
        VerifySecretOperationChallengeCommand request,
        CancellationToken cancellationToken)
    {
        // A keyring change is costed for its action, and only for the action the
        // confirmation was raised for: the digest is recomputed from the ring the file
        // holds now, and a claim of another action, key or value is refused before the
        // approval opens, so the confirmation stays unverified and nothing is spendable,
        // the action it was raised for included (SEC-B5).
        var scope = new KeyringImpactScope(IssuedBefore: null);
        Func<SecretOperationChallenge, CancellationToken, Task<ErrorOr<Success>>>? keyringBinding = null;
        if (request.Action is { } action)
        {
            keyringBinding = async (pending, token) =>
            {
                var bound = await KeyringBindingAsync(action, request, pending.Operation, pending.PayloadHash, token);
                if (bound.IsError)
                {
                    return bound.Errors;
                }

                scope = bound.Value;
                return Result.Success;
            };
        }

        var verified = await _challengeService.VerifyAsync(
            request.ChallengeId, request.Code, request.RequestedBy, cancellationToken, keyringBinding);

        if (verified.IsError)
        {
            return verified.Errors;
        }

        var challenge = verified.Value;

        // An access token is only "live" for its lifetime plus the validation
        // clock skew — past that, the holder is getting a new one anyway and was
        // never going to notice the rotation.
        var accessTokenLifetime =
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenLifetimeMinutes) + _jwtSettings.ClockSkew;

        var snapshot = await _impactRepository.GetImpactAsync(
            accessTokenLifetime, scope.IssuedBefore, PurposeLimitedOverlap.LegacyCap, cancellationToken);

        var impact = SecretRotationImpact.Build(
            challenge.Operation, request.Action, snapshot, challenge.ApprovalExpiresAt!.Value, scope.KeyStillAccepted);

        _logger.LogWarning(
            "Secret operation {Operation} costed for {UserId}: {AffectedUsers} users affected",
            challenge.Operation, request.RequestedBy, impact.AffectedUsers);

        return impact;
    }

    // Checks the claimed keyring action against the confirmation, through the binding of the
    // ring the confirmation's operation belongs to, and answers which credentials the change
    // can stop.
    private async Task<ErrorOr<KeyringImpactScope>> KeyringBindingAsync(
        KeyringAction action,
        VerifySecretOperationChallengeCommand request,
        SecretOperation operation,
        string? payloadHash,
        CancellationToken cancellationToken)
    {
        if (!KeyringStepUp.Confirms(action, operation) || _keyrings.For(operation) is not { } keyring)
        {
            return SecretErrors.ChallengeNotApproved;
        }

        return await keyring.BindAsync(
            action, new KeyringTarget(request.KeyId, request.Kid), request.Value, payloadHash, cancellationToken);
    }
}
