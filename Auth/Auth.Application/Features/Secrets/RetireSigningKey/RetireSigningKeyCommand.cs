using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.RetireSigningKey;

/// <summary>
/// Command to remove a previous signing key, or a compromised record, for good (S12). The
/// secrets file is saved twice, so its backup copy loses the key too.
/// </summary>
/// <param name="Kid">The key to retire.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record RetireSigningKeyCommand(
    string Kid,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<SigningKeyringDto>>;
