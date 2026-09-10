using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Users.Common;

/// <summary>
/// Enforces the never-recycle identifier policy on every path that creates a
/// user: an email whose hash appears in the destruction registry belongs to a
/// permanently deleted account and can never be registered again. The
/// response is byte-identical to the ordinary "email taken" conflict, so the
/// check leaks nothing about deletion state.
/// </summary>
public class IdentifierReservationGuard
{
    private readonly IAccountDeletionTombstoneRepository _tombstoneRepository;
    private readonly IIdentifierHasher _identifierHasher;

    public IdentifierReservationGuard(
        IAccountDeletionTombstoneRepository tombstoneRepository,
        IIdentifierHasher identifierHasher)
    {
        _tombstoneRepository = tombstoneRepository;
        _identifierHasher = identifierHasher;
    }

    /// <summary>
    /// Returns the standard duplicate-email conflict when the address is
    /// permanently reserved by a tombstone.
    /// </summary>
    public async Task<ErrorOr<Success>> EnsureNotReservedAsync(string email, CancellationToken cancellationToken)
    {
        if (await IsReservedAsync(email, cancellationToken))
        {
            return UserErrors.DuplicateEmail(email);
        }

        return Result.Success;
    }

    /// <summary>
    /// Whether the address is permanently reserved by a tombstone. For the
    /// callers that must not answer differently for a reserved address — a
    /// registration start classifies with this and then does the same work
    /// either way — rather than the conflict above.
    /// </summary>
    public Task<bool> IsReservedAsync(string email, CancellationToken cancellationToken) =>
        _tombstoneRepository.ExistsByEmailHashAsync(_identifierHasher.HashEmail(email), cancellationToken);
}
