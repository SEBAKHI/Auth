using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountExternal;

/// <summary>
/// Handler for external-identity grace-period recovery (passwordless
/// accounts). The provider's verified ID token is the authentication; an
/// unknown identity or an account without a pending deletion returns the
/// identical invalid-credentials error.
/// </summary>
public class RecoverAccountExternalCommandHandler
    : IRequestHandler<RecoverAccountExternalCommand, ErrorOr<LoginResponse>>
{
    private readonly IExternalAuthProviderFactory _providerFactory;
    private readonly IUserExternalLoginRepository _externalLoginRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly AccountDeletionRecoverer _recoverer;

    public RecoverAccountExternalCommandHandler(
        IExternalAuthProviderFactory providerFactory,
        IUserExternalLoginRepository externalLoginRepository,
        IUserRepository userRepository,
        IAccountDeletionRequestRepository requestRepository,
        AccountDeletionRecoverer recoverer)
    {
        _providerFactory = providerFactory;
        _externalLoginRepository = externalLoginRepository;
        _userRepository = userRepository;
        _requestRepository = requestRepository;
        _recoverer = recoverer;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        RecoverAccountExternalCommand request, CancellationToken cancellationToken)
    {
        var provider = _providerFactory.GetProvider(request.Provider);
        if (provider is null)
        {
            return ExternalAuthErrors.ProviderNotSupported(request.Provider);
        }

        var tokenResult = await provider.ValidateTokenAsync(request.IdToken, request.Nonce, cancellationToken);
        if (tokenResult.IsError)
        {
            return tokenResult.Errors;
        }

        if (!tokenResult.Value.EmailVerified)
        {
            return ExternalAuthErrors.EmailNotVerifiedByProvider;
        }

        var externalLogin = await _externalLoginRepository.GetByProviderAsync(
            request.Provider, tokenResult.Value.ProviderUserId, cancellationToken);
        if (externalLogin is null)
        {
            return UserErrors.InvalidCredentials;
        }

        var user = await _userRepository.GetByIdIncludeDeletedAsync(externalLogin.UserId, cancellationToken);
        if (user is null || !user.IsDeleted)
        {
            return UserErrors.InvalidCredentials;
        }

        if (user.IsLockedOut())
        {
            return UserErrors.AccountLockedUntil(user.LockoutEnd);
        }

        var deletionRequest = await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (deletionRequest is null || deletionRequest.Status != AccountDeletionStatus.PendingGrace)
        {
            return UserErrors.InvalidCredentials;
        }

        return await _recoverer.RecoverAsync(
            user, deletionRequest, request.TwoFactorCode, request.IpAddress, request.UserAgent,
            request.DeviceId, cancellationToken);
    }
}
