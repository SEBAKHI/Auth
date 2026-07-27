using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Handler for external authentication login/registration.
/// Validates the provider's ID token, then either logs in an existing user
/// or creates a new account with optional personal organization.
/// </summary>
public class ExternalLoginCommandHandler : IRequestHandler<ExternalLoginCommand, ErrorOr<LoginResponse>>
{
    private readonly IExternalAuthProviderFactory _providerFactory;
    private readonly IUserExternalLoginRepository _externalLoginRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _accountDeletionRequestRepository;
    private readonly IdentifierReservationGuard _reservationGuard;
    private readonly IPersonalOrganizationCreator _personalOrganizationCreator;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly ITwoFactorChallengeService _twoFactorChallengeService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<ExternalLoginCommandHandler> _logger;

    public ExternalLoginCommandHandler(
        IExternalAuthProviderFactory providerFactory,
        IUserExternalLoginRepository externalLoginRepository,
        IUserRepository userRepository,
        IAccountDeletionRequestRepository accountDeletionRequestRepository,
        IdentifierReservationGuard reservationGuard,
        IPersonalOrganizationCreator personalOrganizationCreator,
        ILoginResponseBuilder loginResponseBuilder,
        ITwoFactorChallengeService twoFactorChallengeService,
        IDomainEventDispatcher eventDispatcher,
        ILogger<ExternalLoginCommandHandler> logger)
    {
        _providerFactory = providerFactory;
        _externalLoginRepository = externalLoginRepository;
        _userRepository = userRepository;
        _accountDeletionRequestRepository = accountDeletionRequestRepository;
        _reservationGuard = reservationGuard;
        _personalOrganizationCreator = personalOrganizationCreator;
        _loginResponseBuilder = loginResponseBuilder;
        _twoFactorChallengeService = twoFactorChallengeService;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        ExternalLoginCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve provider
        var provider = _providerFactory.GetProvider(request.Provider);
        if (provider is null)
            return ExternalAuthErrors.ProviderNotSupported(request.Provider);

        // 2. Validate ID token (signature, expiry, audience, nonce)
        var tokenResult = await provider.ValidateTokenAsync(request.IdToken, request.Nonce, cancellationToken);
        if (tokenResult.IsError)
            return tokenResult.Errors;

        var externalUser = tokenResult.Value;

        // 3. SECURITY: Reject unverified emails to prevent account hijacking
        if (!externalUser.EmailVerified)
        {
            _logger.LogWarning(
                "External login rejected: email not verified by provider {Provider} for {Email}",
                request.Provider, externalUser.Email);
            return ExternalAuthErrors.EmailNotVerifiedByProvider;
        }

        // 4. Check if external login already exists
        var existingExternalLogin = await _externalLoginRepository.GetByProviderAsync(
            request.Provider, externalUser.ProviderUserId, cancellationToken);

        User? user;

        if (existingExternalLogin != null)
        {
            // Returning user — fetch and validate
            user = await _userRepository.GetByIdAsync(existingExternalLogin.UserId, cancellationToken);
            if (user == null)
            {
                // A pending-deletion account is invisible to the normal lookup;
                // the verified provider token proves identity, so surface the
                // recovery path instead of a dead end.
                var pendingSignal = await GetPendingDeletionSignalAsync(
                    existingExternalLogin.UserId, cancellationToken);
                if (pendingSignal is not null)
                    return pendingSignal.Value;

                return UserErrors.NotFound(existingExternalLogin.UserId);
            }

            // Update cached provider info
            existingExternalLogin.UpdateFromProvider(
                externalUser.Email, externalUser.DisplayName, externalUser.PictureUrl);
            await _externalLoginRepository.UpdateAsync(existingExternalLogin, cancellationToken);

            _logger.LogInformation(
                "Existing user {UserId} logged in via {Provider}",
                user.Id, request.Provider);
        }
        else
        {
            // New external login — check if user exists by email
            user = await _userRepository.GetByEmailAsync(externalUser.Email, cancellationToken);

            if (user != null)
            {
                // Link external provider to existing user
                _logger.LogInformation(
                    "Linking {Provider} to existing user {UserId} ({Email})",
                    request.Provider, user.Id, user.Email);
            }
            else
            {
                // A pending-deletion account with this email is hidden from the
                // lookup above; creating a second account would collide on the
                // unique email constraint, so surface the recovery path instead.
                var deletedByEmail = await _userRepository.GetByEmailIncludeDeletedAsync(
                    externalUser.Email, cancellationToken);
                if (deletedByEmail is { IsDeleted: true })
                {
                    var pendingSignal = await GetPendingDeletionSignalAsync(deletedByEmail.Id, cancellationToken);
                    return pendingSignal ?? UserErrors.DuplicateEmail(externalUser.Email);
                }

                // The never-recycle policy: a permanently deleted identifier can
                // never be registered again (same response as a duplicate).
                var reservation = await _reservationGuard.EnsureNotReservedAsync(
                    externalUser.Email, cancellationToken);
                if (reservation.IsError)
                    return reservation.Errors;

                // Create new user from external provider
                user = User.CreateFromExternalProvider(
                    email: externalUser.Email,
                    firstName: externalUser.FirstName,
                    lastName: externalUser.LastName,
                    createdBy: Guid.Empty,
                    displayName: externalUser.DisplayName,
                    profileImageUrl: externalUser.PictureUrl);

                await _userRepository.CreateAsync(user, cancellationToken);

                // Optionally create personal organization
                if (request.CreateOrganization)
                {
                    await _personalOrganizationCreator.CreateAsync(user, cancellationToken);
                }

                _logger.LogInformation(
                    "New user {UserId} registered via {Provider} ({Email})",
                    user.Id, request.Provider, user.Email);
            }

            // Create external login record
            var externalLogin = UserExternalLogin.Create(
                userId: user.Id,
                provider: request.Provider,
                providerUserId: externalUser.ProviderUserId,
                email: externalUser.Email,
                name: externalUser.DisplayName,
                pictureUrl: externalUser.PictureUrl);

            await _externalLoginRepository.CreateAsync(externalLogin, cancellationToken);
        }

        // Check account status
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
            return statusCheck.Errors;

        // Check lockout
        if (user.IsLockedOut())
            return UserErrors.AccountLockedUntil(user.LockoutEnd);

        // Auto-unlock if lockout has expired
        if (user.Status == UserStatus.Locked)
        {
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByIdAsync(user.Id, cancellationToken))!;
        }

        // Two-factor gate: the user opted into 2FA, so a provider login must
        // not bypass it. No tokens are issued until the code is verified.
        if (user.TwoFactorEnabled)
        {
            var challengeToken = await _twoFactorChallengeService.CreateChallengeAsync(
                user, request.IpAddress, cancellationToken);

            _logger.LogInformation(
                "Two-factor verification pending for external login of user {UserId} via {Provider}",
                user.Id, request.Provider);

            return new LoginResponse
            {
                RequiresTwoFactor = true,
                TwoFactorChallengeToken = challengeToken
            };
        }

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        // Build device info and return login response
        var deviceInfo = AuthenticationHelper.BuildDeviceInfo(request.UserAgent, request.DeviceId);
        var loginResponse = await _loginResponseBuilder.BuildAsync(user, request.IpAddress, deviceInfo, cancellationToken);

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return loginResponse;
    }

    /// <summary>
    /// Returns the pending-deletion error (with the grace deadline) when the
    /// account awaits deletion — callers have already proven identity via the
    /// provider's verified token.
    /// </summary>
    private async Task<Error?> GetPendingDeletionSignalAsync(Guid userId, CancellationToken cancellationToken)
    {
        var active = await _accountDeletionRequestRepository.GetActiveByUserIdAsync(userId, cancellationToken);
        return active is { Status: AccountDeletionStatus.PendingGrace }
            ? UserErrors.AccountPendingDeletion(active.GraceEndsAtUtc)
            : null;
    }
}
