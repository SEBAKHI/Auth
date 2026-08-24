using System.Globalization;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Application.Features.Authentication.SendEmailVerification;
using Auth.Application.Features.Users.Common;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Register;

/// <summary>
/// Handler for public self-registration.
/// Creates a user, optionally creates a personal organization, and sends email verification.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ErrorOr<RegisterResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly IdentifierReservationGuard _reservationGuard;
    private readonly IPersonalOrganizationCreator _personalOrganizationCreator;
    private readonly IMediator _mediator;
    private readonly IDomainEventDispatcher _eventDispatcher;

    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IdentifierReservationGuard reservationGuard,
        IPersonalOrganizationCreator personalOrganizationCreator,
        IMediator mediator,
        IDomainEventDispatcher eventDispatcher,

        ILogger<RegisterCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _reservationGuard = reservationGuard;
        _personalOrganizationCreator = personalOrganizationCreator;
        _mediator = mediator;
        _eventDispatcher = eventDispatcher;

        _logger = logger;
    }

    public async Task<ErrorOr<RegisterResponse>> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken)
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
        var passwordValidation = _passwordValidator.Validate(request.Password);
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

        // Create user (self-registration: CreatedBy = Guid.Empty)
        var user = User.Create(
            email: request.Email,
            passwordHash: passwordHash,
            firstName: request.FirstName,
            lastName: request.LastName,
            createdBy: Guid.Empty,
            phoneNumber: request.PhoneNumber,
            // Site language becomes the durable preference: explicit choice from
            // the client, else the request culture (X-Language/Accept-Language) —
            // verification and later notifications follow this language.
            preferredLanguage: Languages.Normalize(request.PreferredLanguage)
                ?? Languages.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                ?? Languages.Default,
            timeZone: request.TimeZone ?? "UTC");

        await _userRepository.CreateAsync(user, cancellationToken);




        // User.Create raises UserCreatedEvent, and nothing here ever dispatched it, so a


        // self-registered account appeared with no audit row at all — the one event class


        // where the actor is a stranger rather than a known administrator.


        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);
        // Optionally create personal organization
        var organizationCreated = false;
        if (request.CreateOrganization)
        {
            organizationCreated = await _personalOrganizationCreator.CreateAsync(user, cancellationToken);
        }

        // Send verification email
        var verificationResult = await _mediator.Send(
            new SendEmailVerificationCommand(user.Id),
            cancellationToken);

        var maskedEmail = EmailMasking.Mask(user.Email);

        DateTime? verificationCodeExpiresAt = null;
        if (verificationResult.IsError)
        {
            _logger.LogWarning(
                "User {UserId} registered but verification email failed: {Error}",
                user.Id, verificationResult.FirstError.Description);
        }
        else
        {
            verificationCodeExpiresAt = verificationResult.Value.ExpiresAt;
        }

        _logger.LogInformation(
            "User registered: {UserId} ({Email})",
            user.Id, user.Email);

        return new RegisterResponse(
            user.Id,
            maskedEmail,
            "Registration successful. Please verify your email to sign in.",
            organizationCreated,
            verificationCodeExpiresAt);
    }
}
