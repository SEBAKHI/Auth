using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Application.Features.Authentication.SendEmailVerification;
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
    private readonly IPersonalOrganizationCreator _personalOrganizationCreator;
    private readonly IMediator _mediator;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IPersonalOrganizationCreator personalOrganizationCreator,
        IMediator mediator,
        ILogger<RegisterCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _personalOrganizationCreator = personalOrganizationCreator;
        _mediator = mediator;
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
            displayName: request.DisplayName,
            phoneNumber: request.PhoneNumber,
            preferredLanguage: request.PreferredLanguage ?? "en",
            timeZone: request.TimeZone ?? "UTC");

        await _userRepository.CreateAsync(user, cancellationToken);

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

        var maskedEmail = MaskEmail(user.Email);

        if (verificationResult.IsError)
        {
            _logger.LogWarning(
                "User {UserId} registered but verification email failed: {Error}",
                user.Id, verificationResult.FirstError.Description);
        }

        _logger.LogInformation(
            "User registered: {UserId} ({Email})",
            user.Id, user.Email);

        return new RegisterResponse(
            user.Id,
            maskedEmail,
            "Registration successful. Please verify your email to sign in.",
            organizationCreated);
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        if (localPart.Length <= 2)
            return $"{localPart[0]}***{domain}";

        return $"{localPart[0]}{new string('*', Math.Min(localPart.Length - 2, 4))}{localPart[^1]}{domain}";
    }
}
