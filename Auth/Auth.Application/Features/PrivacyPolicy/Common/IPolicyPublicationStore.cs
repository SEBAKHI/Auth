using Auth.Domain.Entities;
using ErrorOr;

namespace Auth.Application.Features.PrivacyPolicy.Common;

/// <summary>
/// Prepares the immutable HTML produced by a policy publish for direct static
/// delivery from the Accounts origin.
/// </summary>
public interface IPolicyPublicationStore
{
    /// <summary>
    /// Writes a complete candidate set without changing the files readers see.
    /// The returned publication activates that set and restores the previous
    /// files automatically unless <see cref="IPolicyFilePublication.Complete"/>
    /// is called after the database transaction succeeds.
    /// </summary>
    Task<ErrorOr<IPolicyFilePublication>> StageAsync(
        string version,
        IReadOnlyList<PrivacyPolicyArtifact> artifacts,
        CancellationToken cancellationToken);
}

/// <summary>
/// A prepared filesystem publication with compensating rollback semantics.
/// </summary>
public interface IPolicyFilePublication : IDisposable
{
    /// <summary>
    /// Replaces the public files with their prepared bytes, restoring every
    /// file already changed if any replacement fails.
    /// </summary>
    ErrorOr<Success> Activate();

    /// <summary>
    /// Confirms that the corresponding database transaction succeeded. After
    /// this call disposal cleans temporary files without restoring the prior
    /// public revision.
    /// </summary>
    void Complete();
}
