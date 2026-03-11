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
/// Creates a user, auto-creates a personal organization, and sends email verification.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, ErrorOr<RegisterResponse>>
{
    private const string OrgOwnerRoleCode = "org-owner";

    private readonly IUserRepository _userRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IMediator _mediator;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IMediator mediator,
        ILogger<RegisterCommandHandler> logger)
    {
        _userRepository = userRepository;
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
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

        // Auto-create personal organization
        await CreatePersonalOrganizationAsync(user, cancellationToken);

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
            "Registration successful. Please verify your email to sign in.");
    }

    private async Task CreatePersonalOrganizationAsync(User user, CancellationToken cancellationToken)
    {
        // Get the org-owner role (null applicationId for organization-level roles)
        var ownerRole = await _roleRepository.GetByCodeAsync((Guid?)null, OrgOwnerRoleCode, cancellationToken);
        if (ownerRole == null)
        {
            _logger.LogError(
                "Organization owner role '{RoleCode}' not found in database. Skipping org creation for user {UserId}",
                OrgOwnerRoleCode, user.Id);
            return;
        }

        // Generate a unique org code
        var orgCode = GenerateOrgCode(user.FirstName, user.LastName);

        // Ensure code uniqueness
        while (await _organizationRepository.ExistsByCodeAsync(orgCode, cancellationToken))
        {
            orgCode = GenerateOrgCode(user.FirstName, user.LastName);
        }

        var organization = Organization.Create(
            code: orgCode,
            name: $"{user.FirstName}'s Organization",
            contactEmail: user.Email,
            ownerId: user.Id);

        await _organizationRepository.CreateAsync(organization, cancellationToken);

        // Add user as org-owner
        var membership = OrganizationUser.Create(
            organizationId: organization.Id,
            userId: user.Id,
            roleId: ownerRole.Id,
            invitedBy: user.Id);

        await _organizationRepository.AddMemberAsync(membership, cancellationToken);

        _logger.LogInformation(
            "Personal organization created: {OrganizationId} ({OrganizationCode}) for user {UserId}",
            organization.Id, organization.Code, user.Id);
    }

    private static string GenerateOrgCode(string firstName, string lastName)
    {
        var basePart = $"{firstName}-{lastName}"
            .ToLowerInvariant()
            .Replace(" ", "-");

        // Remove invalid characters (keep only lowercase letters, digits, hyphens)
        var cleanCode = new string(basePart
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray())
            .Trim('-');

        // Append a short unique suffix
        var suffix = Guid.NewGuid().ToString("N")[..6];

        return string.IsNullOrEmpty(cleanCode)
            ? $"org-{suffix}"
            : $"{cleanCode}-{suffix}";
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
