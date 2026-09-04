using Auth.Application.Common;
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
    private readonly IPermissionRepository _permissionRepository;
    private readonly IAccountDeletionRequestRepository _accountDeletionRequestRepository;
    private readonly IdentifierReservationGuard _reservationGuard;
    private readonly IEnumerable<IExternalTokenLifecycle> _tokenLifecycles;
    private readonly IPerUserCryptoService _perUserCrypto;
    private readonly IExternalAvatarImporter _avatarImporter;
    private readonly IPersonalOrganizationCreator _personalOrganizationCreator;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly ITwoFactorChallengeService _twoFactorChallengeService;
    private readonly ExternalNonceGuard _nonceGuard;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly Auth.Application.Configuration.PasswordSettings _passwordSettings;
    private readonly Auth.Application.Configuration.RegistrationSettings _registrationSettings;
    private readonly ILogger<ExternalLoginCommandHandler> _logger;

    public ExternalLoginCommandHandler(
        IExternalAuthProviderFactory providerFactory,
        IUserExternalLoginRepository externalLoginRepository,
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        IAccountDeletionRequestRepository accountDeletionRequestRepository,
        IdentifierReservationGuard reservationGuard,
        IEnumerable<IExternalTokenLifecycle> tokenLifecycles,
        IPerUserCryptoService perUserCrypto,
        IExternalAvatarImporter avatarImporter,
        IPersonalOrganizationCreator personalOrganizationCreator,
        ILoginResponseBuilder loginResponseBuilder,
        ITwoFactorChallengeService twoFactorChallengeService,
        ExternalNonceGuard nonceGuard,
        IDomainEventDispatcher eventDispatcher,
        ILoginAttemptRepository loginAttemptRepository,
        Microsoft.Extensions.Options.IOptionsSnapshot<Auth.Application.Configuration.PasswordSettings> passwordSettings,
        Microsoft.Extensions.Options.IOptionsSnapshot<Auth.Application.Configuration.RegistrationSettings> registrationSettings,
        ILogger<ExternalLoginCommandHandler> logger)
    {
        _providerFactory = providerFactory;
        _externalLoginRepository = externalLoginRepository;
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _passwordSettings = passwordSettings.Value;
        _registrationSettings = registrationSettings.Value;
        _permissionRepository = permissionRepository;
        _accountDeletionRequestRepository = accountDeletionRequestRepository;
        _reservationGuard = reservationGuard;
        _tokenLifecycles = tokenLifecycles;
        _perUserCrypto = perUserCrypto;
        _avatarImporter = avatarImporter;
        _personalOrganizationCreator = personalOrganizationCreator;
        _loginResponseBuilder = loginResponseBuilder;
        _twoFactorChallengeService = twoFactorChallengeService;
        _nonceGuard = nonceGuard;
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

        // 2. The nonce must be one this server issued to this browser. Checked
        // BEFORE the token is validated: a value the caller invented tells us
        // nothing about the token, so there is no point examining the token
        // against it.
        var nonceCheck = _nonceGuard.Validate(request.Nonce, request.NonceCookie);
        if (nonceCheck.IsError)
        {
            _logger.LogWarning(
                "External login rejected for provider {Provider}: the nonce was absent or not issued to this browser",
                request.Provider);
            return nonceCheck.Errors;
        }

        // 3. Validate ID token (signature, expiry, audience, nonce)
        var tokenResult = await provider.ValidateTokenAsync(request.IdToken, request.Nonce, cancellationToken);
        if (tokenResult.IsError)
            return tokenResult.Errors;

        var externalUser = tokenResult.Value;

        // 3. SECURITY: Reject unverified emails to prevent account hijacking
        if (!externalUser.EmailVerified)
        {
            _logger.LogWarning(
                "External login rejected: email not verified by provider {Provider} for {Email}",
                request.Provider, EmailMasking.Mask(externalUser.Email));
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

            // Accounts that predate the import, or whose earlier attempt failed, pick
            // the picture up here. One import per account: the guard inside sees a key
            // and does nothing on every later sign-in.
            await AdoptProviderAvatarAsync(user, externalUser.PictureUrl, cancellationToken);

            _logger.LogInformation(
                "Existing user {UserId} logged in via {Provider}",
                user.Id, request.Provider);
        }
        else
        {
            // New external login — check if user exists by email
            user = await _userRepository.GetByEmailAsync(externalUser.Email, cancellationToken);
            var linkedToExistingAccount = user != null;

            if (user != null)
            {
                // Link external provider to existing user
                _logger.LogInformation(
                    "Linking {Provider} to existing user {UserId} ({Email})",
                    request.Provider, user.Id, EmailMasking.Mask(user.Email));

                // An account registered by email and only now linked to a provider has
                // no picture of its own; this is where it gets one.
                await AdoptProviderAvatarAsync(user, externalUser.PictureUrl, cancellationToken);
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

                // The second door. Reaching here means a valid provider identity
                // matched no account here, so the next lines would CREATE one —
                // self-registration by another route, and the one an operator
                // who closed public sign-up would otherwise never notice was
                // still open. Refused before the avatar import, which is an
                // outbound request made on the caller's behalf.
                //
                // Only this branch is gated. An account that already exists
                // keeps signing in, and a provider still links to an account
                // with the same address: closing the door creates no accounts,
                // it does not lock out the accounts already through it.
                if (!_registrationSettings.AllowExternalProvisioning)
                {
                    _logger.LogInformation(
                        "External provisioning refused for {Email} via {Provider} from a closed server",
                        EmailMasking.Mask(externalUser.Email), request.Provider);
                    return UserErrors.ExternalRegistrationClosed;
                }

                // Create new user from external provider. Apple never puts the
                // name in the ID token — it arrives client-side on the FIRST
                // authorization only, so the request fields fill the gap here
                // (first registration) and are ignored everywhere else.
                // Imported before the account exists so the storage key goes in on the
                // insert. What is stored is our own key, never the provider's URL: the
                // apps' img-src names this origin only.
                var importedAvatarKey = await _avatarImporter.TryImportAsync(
                    externalUser.PictureUrl, cancellationToken);

                user = User.CreateFromExternalProvider(
                    email: externalUser.Email,
                    firstName: FirstNonEmpty(externalUser.FirstName, request.GivenName),
                    lastName: FirstNonEmpty(externalUser.LastName, request.FamilyName),
                    createdBy: Guid.Empty,
                    profileImageUrl: importedAvatarKey);

                await _userRepository.CreateAsync(user, cancellationToken);

                // Optionally create personal organization
                if (request.CreateOrganization)
                {
                    await _personalOrganizationCreator.CreateAsync(user, cancellationToken);
                }

                _logger.LogInformation(
                    "New user {UserId} registered via {Provider} ({Email})",
                    user.Id, request.Provider, EmailMasking.Mask(user.Email));
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
            existingExternalLogin = externalLogin;

            if (linkedToExistingAccount)
            {
                await RecordProviderLinkAsync(
                    user, request.Provider, externalUser.ProviderUserId, cancellationToken);
            }
        }

        // Store the provider's revocable refresh token (Apple) for
        // deletion-time revocation. Best-effort by design: a failed exchange
        // must never break the sign-in — the account simply has no token to
        // revoke later, which the destruction audit records.
        if (!string.IsNullOrEmpty(request.AuthorizationCode))
        {
            await StoreProviderRefreshTokenAsync(
                existingExternalLogin!, user.Id, request.Provider, request.AuthorizationCode, cancellationToken);
        }

        // Check account status
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
            return statusCheck.Errors;

        // Check lockout — for strangers only, and only for the automatic lock.
        // A provider sign-in proves identity by the provider's token rather than
        // by a guessable password, so when the failure counter raised the lock
        // the owner's own device (an address they signed in from within thirty
        // days, or a device with a live session) is not shut out by strangers'
        // wrong passwords; an administrator's lock stands for everyone. See
        // LoginCommandHandler for the rule and its per-source ceiling.
        if (user.IsLockedOut())
        {
            var familiar = user.IsLockedByFailedAttempts(_passwordSettings.MaxFailedAttempts)
                && await AuthenticationHelper.IsFamiliarSourceAsync(
                    _loginAttemptRepository, user.Id, request.IpAddress, request.DeviceId, cancellationToken);
            if (!familiar)
                return UserErrors.AccountLockedUntil(user.LockoutEnd);

            // The provider has already vouched for this person: clear the
            // strangers' lock in full rather than leave a Status=Locked row that
            // credential renewal would refuse as indefinite.
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByIdAsync(user.Id, cancellationToken))!;
        }
        // Auto-unlock if lockout has expired
        else if (user.Status == UserStatus.Locked)
        {
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByIdAsync(user.Id, cancellationToken))!;
        }

        // Two-factor gate: the user opted into 2FA, so a provider login must
        // not bypass it. No tokens are issued until the code is verified.
        if (user.TwoFactorEnabled)
        {
            var challengeToken = await _twoFactorChallengeService.CreateChallengeAsync(
                user, request.IpAddress, request.UserAgent, cancellationToken);

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

        // Return login response
        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken);

        if (loginResponse.IsError)
        {
            // At the concurrent session limit. The pending UserLoggedInEvent is
            // dropped rather than dispatched: nothing logged in.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return loginResponse;
    }

    /// <summary>
    /// Adopts the provider's picture as the account avatar the first time one is seen,
    /// copying it into this system's own image storage.
    /// </summary>
    /// <remarks>
    /// A picture already on the account is never replaced — whether it was uploaded by
    /// the user or imported earlier — so this costs one fetch per account and nothing
    /// afterwards. A failed import is a no-op: the account keeps its initials and the
    /// next sign-in tries again.
    /// </remarks>
    private async Task AdoptProviderAvatarAsync(
        User user, string? pictureUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(user.ProfileImageUrl))
        {
            return;
        }

        var storageKey = await _avatarImporter.TryImportAsync(pictureUrl, cancellationToken);
        if (storageKey is null)
        {
            return;
        }

        user.SetProfileImage(storageKey, user.Id);
        await _userRepository.UpdateAsync(user, cancellationToken);
    }

    private static string FirstNonEmpty(string providerValue, string? requestValue) =>
        !string.IsNullOrWhiteSpace(providerValue) ? providerValue : requestValue?.Trim() ?? "";

    /// <summary>
    /// Announces that a provider was attached to an account that already existed.
    /// </summary>
    /// <remarks>
    /// Dispatched HERE rather than at the end of the sign-in, and that placement is the whole
    /// point. The link row is committed by this line, but the two-factor gate and the
    /// session-limit check both return before the dispatch at the end and drop whatever is
    /// pending, on the stated grounds that nobody logged in. The link happened regardless — so
    /// deferring would have silenced this event for precisely the hardened accounts it exists
    /// to report. Nothing else is queued on the aggregate on this path: it was loaded by email
    /// a few lines above and only its avatar has been touched, which raises nothing.
    ///
    /// The wildcard lookup runs only on this branch, which is once per account per provider.
    /// </remarks>
    private async Task RecordProviderLinkAsync(
        User user,
        string provider,
        string providerUserId,
        CancellationToken cancellationToken)
    {
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
            user.Id, cancellationToken);

        // Same semantics the authorization handler enforces: "*" grants everything and
        // "prefix:*" grants a whole area. Either one makes this link worth a warning.
        var holdsWildcard = permissions.Any(code =>
            code == "*" || code.EndsWith(":*", StringComparison.Ordinal));

        user.RecordExternalProviderLink(provider, providerUserId, holdsWildcard);
        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);
    }

    /// <summary>
    /// Exchanges the sign-in authorization code for the provider's refresh
    /// token and stores it encrypted under the user's DEK (crypto-shredded
    /// with the account). No-op for providers without a token lifecycle.
    /// </summary>
    private async Task StoreProviderRefreshTokenAsync(
        UserExternalLogin externalLogin,
        Guid userId,
        string provider,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var lifecycle = _tokenLifecycles.FirstOrDefault(
            l => string.Equals(l.ProviderName, provider, StringComparison.OrdinalIgnoreCase));
        if (lifecycle is null)
        {
            return;
        }

        var refreshToken = await lifecycle.ExchangeCodeAsync(authorizationCode, cancellationToken);
        if (refreshToken is null)
        {
            _logger.LogWarning(
                "No {Provider} refresh token stored for user {UserId}: the code exchange failed or returned none — deletion-time revocation will be unavailable",
                provider, userId);
            return;
        }

        var encrypted = await _perUserCrypto.EncryptAsync(
            userId, refreshToken, EncryptedFieldPurpose.ExternalProviderRefreshToken, cancellationToken);
        await _externalLoginRepository.UpdateProviderRefreshTokenAsync(
            externalLogin.Id, encrypted, cancellationToken);
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
