namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The single-use record of client assertions (OI-128; RFC 7523 §3, item 7): each assertion's
/// <c>jti</c> is accepted once per application.
/// </summary>
public interface IClientAssertionJtiStore
{
    /// <summary>
    /// Records <paramref name="jti"/> for <paramref name="applicationId"/> unless it is already
    /// recorded, in one conditional statement.
    /// </summary>
    /// <param name="applicationId">The application whose key signed the assertion.</param>
    /// <param name="jti">The assertion's identifier, as sent.</param>
    /// <param name="expiresAtUtc">The assertion's <c>exp</c>; the row is not needed after it.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>True for the first use; false for a replay.</returns>
    Task<bool> TryConsumeAsync(Guid applicationId, string jti, DateTime expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> rows that expired before <paramref name="olderThanUtc"/>.</summary>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
