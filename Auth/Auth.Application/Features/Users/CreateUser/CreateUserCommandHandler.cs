using Auth.Application.Common;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Application.Validators;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.CreateUser;

/// <summary>
/// Handler for creating a new user.
/// </summary>
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly IdentifierReservationGuard _reservationGuard;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly IPendingRegistrationConsumer _pendingRegistrationConsumer;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IdentifierReservationGuard reservationGuard,
        IDomainEventDispatcher eventDispatcher,
        IPendingRegistrationConsumer pendingRegistrationConsumer,
        ILogger<CreateUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _reservationGuard = reservationGuard;
        _eventDispatcher = eventDispatcher;
        _pendingRegistrationConsumer = pendingRegistrationConsumer;
        _logger = logger;
    }

    public async Task<ErrorOr<UserDto>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate email
        if (await _userRepository.ExistsByEmailAsync(request.Email, cancellationToken))
        {
            return UserErrors.DuplicateEmail(request.Email);
        }

        // The never-recycle policy: a permanently deleted identifier can never
        // be registered again (same response as an ordinary duplicate).
        var reservation = await _reservationGuard.EnsureNotReservedAsync(request.Email, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        // Validate password
        var passwordValidation = _passwordValidator.Validate(request.Password, nameof(request.Password));
        if (passwordValidation.IsError)
        {
            return passwordValidation.Errors;
        }

        // Breached-password policy (no-op when disabled; may warn-and-allow or reject)
        var breachResult = await _breachEvaluator.EvaluateAsync(request.Password, cancellationToken);
        if (breachResult.IsError)
        {
            return breachResult.Errors;
        }

        // Hash password
        var passwordHash = _passwordHasher.HashPassword(request.Password);

        // Create user
        var user = User.Create(
            email: request.Email,
            passwordHash: passwordHash,
            firstName: request.FirstName,
            lastName: request.LastName,
            createdBy: request.CreatedBy,
            phoneNumber: request.PhoneNumber,
            preferredLanguage: request.PreferredLanguage ?? "en",
            timeZone: request.TimeZone ?? "UTC",
            theme: request.Theme ?? "system");

        await _userRepository.CreateAsync(user, cancellationToken);

        // An administrator's policy, not a proof — but a Users row exists for
        // the address now, so a verify-first row pending for it is moot.
        await _pendingRegistrationConsumer.ConsumeAsync(user.Email.Value, cancellationToken);

        _logger.LogInformation(
            "User created: {UserId} ({Email}) by {CreatedBy}",
            user.Id, EmailMasking.Mask(user.Email), request.CreatedBy);

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        // Get effective permissions for the user
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, cancellationToken);

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            Status = user.Status,
            EmailConfirmed = user.EmailConfirmed,
            PhoneConfirmed = user.PhoneConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            PreferredLanguage = user.PreferredLanguage,
            TimeZone = user.TimeZone,
            Theme = user.Theme,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            ModifiedAt = user.ModifiedAt,
            // OI-109: an account is created with no role. Roles are granted only
            // through POST api/v1/users/{id}/roles, which runs PermissionGrantGuard
            // and PlatformGrantFactorGuard; users:create alone grants no authority.
            Roles = [],
            Permissions = permissions.ToList()
        };
    }
}
