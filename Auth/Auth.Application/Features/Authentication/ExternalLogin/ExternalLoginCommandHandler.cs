using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
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
    private readonly IPersonalOrganizationCreator _personalOrganizationCreator;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<ExternalLoginCommandHandler> _logger;

    public ExternalLoginCommandHandler(
        IExternalAuthProviderFactory providerFactory,
        IUserExternalLoginRepository externalLoginRepository,
        IUserRepository userRepository,
        IPersonalOrganizationCreator personalOrganizationCreator,
        ILoginResponseBuilder loginResponseBuilder,
        IDomainEventDispatcher eventDispatcher,
        ILogger<ExternalLoginCommandHandler> logger)
    {
        _providerFactory = providerFactory;
        _externalLoginRepository = externalLoginRepository;
        _userRepository = userRepository;
        _personalOrganizationCreator = personalOrganizationCreator;
        _loginResponseBuilder = loginResponseBuilder;
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
                return UserErrors.NotFound(existingExternalLogin.UserId);

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

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        // Build device info and return login response
        var deviceInfo = AuthenticationHelper.BuildDeviceInfo(request.UserAgent, request.DeviceId);
        var loginResponse = await _loginResponseBuilder.BuildAsync(user, request.IpAddress, deviceInfo, cancellationToken);

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return loginResponse;
    }

}
