using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.VerifySecretOperationChallenge;

/// <summary>
/// Command to answer a step-up confirmation with the emailed code. On success
/// the approval window opens and the operation's blast radius is returned — the
/// figures an administrator sees immediately before the final confirmation.
/// </summary>
/// <param name="ChallengeId">The challenge being answered.</param>
/// <param name="Code">The six-digit code from the email.</param>
/// <param name="RequestedBy">The administrator answering, for actor binding.</param>
/// <param name="Action">
/// For a change of the HMAC keyring (S11), the action the confirmation was raised for:
/// the server checks it against the confirmation's digest and costs that action. Null
/// for every other confirmation.
/// </param>
/// <param name="KeyId">The key a reinstate or a retire names.</param>
/// <param name="Value">The key material of a staged import.</param>
public record VerifySecretOperationChallengeCommand(
    Guid ChallengeId,
    string Code,
    Guid RequestedBy,
    KeyringAction? Action = null,
    int? KeyId = null,
    string? Value = null) : IRequest<ErrorOr<SecretRotationImpactDto>>;
