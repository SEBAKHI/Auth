using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;

/// <summary>
/// Handler returning every recorded policy revision, newest first.
/// </summary>
public class GetPrivacyPolicyVersionsQueryHandler
    : IRequestHandler<GetPrivacyPolicyVersionsQuery, ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;

    public GetPrivacyPolicyVersionsQueryHandler(IPrivacyPolicyVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>> Handle(
        GetPrivacyPolicyVersionsQuery request, CancellationToken cancellationToken)
    {
        var versions = await _repository.GetAllAsync(cancellationToken);

        IReadOnlyList<PrivacyPolicyVersionDto> dtos = versions
            .Select(v => new PrivacyPolicyVersionDto
            {
                Id = v.Id,
                Version = v.Version,
                EffectiveDateUtc = v.EffectiveDateUtc,
                NotifiedAtUtc = v.NotifiedAtUtc,
                NotifiedCount = v.NotifiedCount,
                CreatedAt = v.CreatedAt
            })
            .ToList();

        return ErrorOrFactory.From(dtos);
    }
}
