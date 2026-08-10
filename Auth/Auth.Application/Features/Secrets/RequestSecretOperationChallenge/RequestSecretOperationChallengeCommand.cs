using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.RequestSecretOperationChallenge;

/// <summary>
/// Command to raise a step-up confirmation for a destructive secret operation.
/// Emails a one-time code to the requesting administrator; nothing is rotated.
/// </summary>
/// <param name="Operation">The operation the resulting approval will authorize, and nothing else.</param>
/// <param name="Value">
/// The key material for the import operations, so the confirmation can be bound
/// to the exact bytes being approved. Null for the generate operations.
/// </param>
/// <param name="RequestedBy">The administrator requesting the operation.</param>
/// <param name="IpAddress">The requesting client address, recorded for audit.</param>
public record RequestSecretOperationChallengeCommand(
    SecretOperation Operation,
    string? Value,
    Guid RequestedBy,
    string? IpAddress) : IRequest<ErrorOr<SecretOperationChallengeDto>>;
