using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
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
    private readonly ISecretRotationImpactRepository _impactRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<VerifySecretOperationChallengeCommandHandler> _logger;

    public VerifySecretOperationChallengeCommandHandler(
        SecretOperationChallengeService challengeService,
        ISecretRotationImpactRepository impactRepository,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<VerifySecretOperationChallengeCommandHandler> logger)
    {
        _challengeService = challengeService;
        _impactRepository = impactRepository;
        _jwtSettings = jwtSettings.Value;
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

        // An access token is only "live" for its lifetime plus the validation
        // clock skew — past that, the holder is getting a new one anyway and was
        // never going to notice the rotation.
        var accessTokenLifetime =
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenLifetimeMinutes) + _jwtSettings.ClockSkew;

        var snapshot = await _impactRepository.GetImpactAsync(accessTokenLifetime, cancellationToken);

        var impact = SecretRotationImpact.Build(
            challenge.Operation, snapshot, challenge.ApprovalExpiresAt!.Value);

        _logger.LogWarning(
            "Secret operation {Operation} costed for {UserId}: {AffectedUsers} users affected",
            challenge.Operation, request.RequestedBy, impact.AffectedUsers);

        return impact;
    }
}
