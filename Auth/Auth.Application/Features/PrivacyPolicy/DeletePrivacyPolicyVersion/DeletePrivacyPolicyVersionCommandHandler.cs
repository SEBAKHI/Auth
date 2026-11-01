using Auth.Application.Configuration;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.PrivacyPolicy.DeletePrivacyPolicyVersion;

/// <summary>
/// Handles <see cref="DeletePrivacyPolicyVersionCommand"/>. The entity decides what a draft is;
/// whether deletion records cite the version is a lookup beyond the revision, so it is checked
/// here. The delete statement repeats every condition, so a revision published, announced or
/// cited after it was read here is still refused, never deleted.
/// </summary>
public sealed class DeletePrivacyPolicyVersionCommandHandler
    : IRequestHandler<DeletePrivacyPolicyVersionCommand, ErrorOr<Deleted>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly AccountDeletionSettings _deletionSettings;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<DeletePrivacyPolicyVersionCommandHandler> _logger;

    public DeletePrivacyPolicyVersionCommandHandler(
        IPrivacyPolicyVersionRepository repository,
        IOptionsSnapshot<AccountDeletionSettings> deletionSettings,
        IPublisher publisher,
        TimeProvider time,
        ILogger<DeletePrivacyPolicyVersionCommandHandler> logger)
    {
        _repository = repository;
        _deletionSettings = deletionSettings.Value;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(
        DeletePrivacyPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await _repository.GetByIdAsync(request.VersionId, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.VersionId.ToString());
        }

        var served = await _repository.GetServedVersionIdsAsync(cancellationToken);
        var deletable = version.EnsureDeletable(served.Contains(version.Id), _deletionSettings.PolicyVersion);
        if (deletable.IsError)
        {
            return deletable.Errors;
        }

        if (await _repository.IsCitedByDeletionRecordsAsync(version.Version, cancellationToken))
        {
            return PrivacyPolicyErrors.NotDeletable(version.Version);
        }

        if (!await _repository.TryDeleteDraftAsync(version.Id, _deletionSettings.PolicyVersion, cancellationToken))
        {
            // Published, announced, cited or deleted since it was read.
            return await _repository.GetByIdAsync(version.Id, cancellationToken) is null
                ? PrivacyPolicyErrors.NotFound(version.Version)
                : PrivacyPolicyErrors.NotDeletable(version.Version);
        }

        _logger.LogInformation(
            "Privacy policy draft {Version} ({VersionId}) deleted by {RequestedBy}",
            version.Version, version.Id, request.RequestedBy);

        await _publisher.Publish(
            new PrivacyPolicyVersionDeletedEvent(
                Guid.NewGuid(),
                version.Id,
                version.Version,
                request.RequestedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return Result.Deleted;
    }
}
