using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Security.Keyring;
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
    private readonly HmacKeyringChangeService _keyring;
    private readonly ISecretRotationImpactRepository _impactRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VerifySecretOperationChallengeCommandHandler> _logger;

    public VerifySecretOperationChallengeCommandHandler(
        SecretOperationChallengeService challengeService,
        HmacKeyringChangeService keyring,
        ISecretRotationImpactRepository impactRepository,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        TimeProvider timeProvider,
        ILogger<VerifySecretOperationChallengeCommandHandler> logger)
    {
        _challengeService = challengeService;
        _keyring = keyring;
        _impactRepository = impactRepository;
        _jwtSettings = jwtSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<SecretRotationImpactDto>> Handle(
        VerifySecretOperationChallengeCommand request,
        CancellationToken cancellationToken)
    {
        var verified = await _challengeService.VerifyAsync(
            request.ChallengeId, request.Code, request.RequestedBy, cancellationToken);

        if (verified.IsError)
        {
            return verified.Errors;
        }

        var challenge = verified.Value;

        // A keyring change is costed for its action, and only for the action the
        // confirmation was raised for: the digest is recomputed from the ring the file
        // holds now, and a claim of another action, key or value is refused.
        DateTime? issuedBefore = null;
        if (request.Action is { } action)
        {
            var bound = await KeyringBindingAsync(action, request, challenge.Operation, challenge.PayloadHash, cancellationToken);
            if (bound.IsError)
            {
                return bound.Errors;
            }

            issuedBefore = bound.Value;
        }

        // An access token is only "live" for its lifetime plus the validation
        // clock skew — past that, the holder is getting a new one anyway and was
        // never going to notice the rotation.
        var accessTokenLifetime =
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenLifetimeMinutes) + _jwtSettings.ClockSkew;

        var snapshot = await _impactRepository.GetImpactAsync(
            accessTokenLifetime, issuedBefore, PurposeLimitedOverlap.LegacyCap, cancellationToken);

        var impact = SecretRotationImpact.Build(
            challenge.Operation, request.Action, snapshot, challenge.ApprovalExpiresAt!.Value);

        _logger.LogWarning(
            "Secret operation {Operation} costed for {UserId}: {AffectedUsers} users affected",
            challenge.Operation, request.RequestedBy, impact.AffectedUsers);

        return impact;
    }

    // Checks the claimed keyring action against the confirmation, and answers which
    // credentials the change can stop: for a retire, those issued while the key still
    // issued, or none once its window has closed (DateTime.MinValue counts nothing).
    private async Task<ErrorOr<DateTime?>> KeyringBindingAsync(
        KeyringAction action,
        VerifySecretOperationChallengeCommand request,
        SecretOperation operation,
        string? payloadHash,
        CancellationToken cancellationToken)
    {
        if (!KeyringStepUp.Confirms(action, operation))
        {
            return SecretErrors.ChallengeNotApproved;
        }

        var ring = await _keyring.ReadFileRingAsync(cancellationToken);
        if (ring.IsError)
        {
            return ring.Errors;
        }

        var digest = KeyringStepUp.Digest(action, KeyringStepUp.TargetOf(action, ring.Value, request.KeyId), request.Value);
        if (!string.Equals(digest, payloadHash, StringComparison.Ordinal))
        {
            return SecretErrors.ChallengeNotApproved;
        }

        if (action != KeyringAction.Retire || request.KeyId is not { } keyId || ring.Value.Find(keyId) is not { } key)
        {
            return (DateTime?)null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return ring.Value.BoundedVerifyUntil(key) is { } until && until >= now
            ? key.DemotedAtUtc ?? now
            : DateTime.MinValue;
    }
}
