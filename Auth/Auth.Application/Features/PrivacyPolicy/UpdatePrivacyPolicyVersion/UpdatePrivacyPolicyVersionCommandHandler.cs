using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.UpdatePrivacyPolicyVersion;

/// <summary>
/// Handler updating a revision's identifier, effective date and change note.
///
/// Renaming is allowed ONLY while the revision is an unannounced draft. Once a
/// version is published it stamps deletion requests and tombstones, and once
/// its change notice is sent the number is in users' inboxes — renaming then
/// would silently invalidate the audit trail or contradict what was announced.
/// </summary>
public class UpdatePrivacyPolicyVersionCommandHandler
    : IRequestHandler<UpdatePrivacyPolicyVersionCommand, ErrorOr<PrivacyPolicyVersionDto>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;

    public UpdatePrivacyPolicyVersionCommandHandler(IPrivacyPolicyVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<PrivacyPolicyVersionDto>> Handle(
        UpdatePrivacyPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await _repository.GetByVersionAsync(request.Version, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.Version);
        }

        var rename =
            !string.IsNullOrWhiteSpace(request.NewVersion) &&
            !string.Equals(request.NewVersion, version.Version, StringComparison.Ordinal);

        if (rename)
        {
            if (version.IsPublished || version.NotifiedAtUtc is not null)
            {
                return PrivacyPolicyErrors.VersionLocked(version.Version);
            }

            var clash = await _repository.GetByVersionAsync(request.NewVersion!, cancellationToken);
            if (clash is not null)
            {
                return PrivacyPolicyErrors.DuplicateVersion(request.NewVersion!);
            }

            version.Rename(request.NewVersion!);
        }

        version.UpdateDetails(request.EffectiveDateUtc, request.ChangeNote);
        await _repository.UpdateDetailsAsync(version, cancellationToken);

        return new PrivacyPolicyVersionDto
        {
            Id = version.Id,
            Version = version.Version,
            EffectiveDateUtc = version.EffectiveDateUtc,
            IsPublished = version.IsPublished,
            ChangeNote = version.ChangeNote,
            NotifiedAtUtc = version.NotifiedAtUtc,
            NotifiedCount = version.NotifiedCount,
            CreatedAt = version.CreatedAt
        };
    }
}
