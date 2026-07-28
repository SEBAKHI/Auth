using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.UpdatePrivacyPolicyVersion;

/// <summary>
/// Handler updating a revision's effective date and change note. The version
/// identifier itself is immutable — it stamps tombstones and deletion records,
/// so renaming it would break the audit trail.
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
