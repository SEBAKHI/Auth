using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.CreatePrivacyPolicyVersion;

/// <summary>
/// Handler recording a new policy revision; the unique version index
/// arbitrates duplicates.
/// </summary>
public class CreatePrivacyPolicyVersionCommandHandler
    : IRequestHandler<CreatePrivacyPolicyVersionCommand, ErrorOr<PrivacyPolicyVersionDto>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;

    public CreatePrivacyPolicyVersionCommandHandler(IPrivacyPolicyVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<PrivacyPolicyVersionDto>> Handle(
        CreatePrivacyPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var version = PrivacyPolicyVersion.Create(
            request.Version, request.EffectiveDateUtc, request.ChangeNote, request.RequestedBy);

        if (!await _repository.TryCreateAsync(version, cancellationToken))
        {
            return PrivacyPolicyErrors.DuplicateVersion(request.Version);
        }

        return new PrivacyPolicyVersionDto
        {
            Id = version.Id,
            Version = version.Version,
            EffectiveDateUtc = version.EffectiveDateUtc,
            ChangeNote = version.ChangeNote,
            NotifiedAtUtc = version.NotifiedAtUtc,
            NotifiedCount = version.NotifiedCount,
            CreatedAt = version.CreatedAt
        };
    }
}
