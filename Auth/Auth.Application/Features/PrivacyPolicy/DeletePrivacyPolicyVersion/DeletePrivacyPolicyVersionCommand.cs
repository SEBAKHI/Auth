using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.DeletePrivacyPolicyVersion;

/// <summary>
/// Deletes a draft revision with its written languages. Anything people were shown, told about
/// or that deletions cite is refused (<c>PrivacyPolicy.NotDeletable</c>).
/// </summary>
/// <param name="VersionId">The revision's id (immutable, unlike its "YYYY.MM" label).</param>
public sealed record DeletePrivacyPolicyVersionCommand(Guid VersionId) : IRequest<ErrorOr<Deleted>>
{
    /// <summary>Gets the admin deleting the revision (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
