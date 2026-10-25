using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.GetEmailCodeFactorCount;

/// <summary>
/// Reads the count with the one statement the email posture already uses, so the
/// console's confirmation and the posture warning can never disagree.
/// </summary>
public class GetEmailCodeFactorCountQueryHandler
    : IRequestHandler<GetEmailCodeFactorCountQuery, ErrorOr<EmailCodeFactorCountResponse>>
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;

    public GetEmailCodeFactorCountQueryHandler(ITwoFactorStateStore twoFactorStateStore)
    {
        _twoFactorStateStore = twoFactorStateStore;
    }

    public async Task<ErrorOr<EmailCodeFactorCountResponse>> Handle(
        GetEmailCodeFactorCountQuery request,
        CancellationToken cancellationToken) =>
        new EmailCodeFactorCountResponse(
            await _twoFactorStateStore.CountEmailCodeFactorsAsync(cancellationToken));
}
