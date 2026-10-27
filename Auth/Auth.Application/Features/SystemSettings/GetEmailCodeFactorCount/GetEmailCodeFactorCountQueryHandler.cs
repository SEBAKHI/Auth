using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.GetEmailCodeFactorCount;

/// <summary>
/// Reads the count with the one statement the email posture already uses, so the
/// console's confirmation and the posture warning can never disagree; and beside it
/// the accounts that sign in only by emailed code (OI-215), under the same permission.
/// </summary>
public class GetEmailCodeFactorCountQueryHandler
    : IRequestHandler<GetEmailCodeFactorCountQuery, ErrorOr<EmailCodeFactorCountResponse>>
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IUserRepository _userRepository;

    public GetEmailCodeFactorCountQueryHandler(ITwoFactorStateStore twoFactorStateStore, IUserRepository userRepository)
    {
        _twoFactorStateStore = twoFactorStateStore;
        _userRepository = userRepository;
    }

    public async Task<ErrorOr<EmailCodeFactorCountResponse>> Handle(
        GetEmailCodeFactorCountQuery request,
        CancellationToken cancellationToken) =>
        new EmailCodeFactorCountResponse(
            await _twoFactorStateStore.CountEmailCodeFactorsAsync(cancellationToken),
            await _userRepository.CountEmailCodeOnlyAccountsAsync(cancellationToken));
}
