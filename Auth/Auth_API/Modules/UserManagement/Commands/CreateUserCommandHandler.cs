using Auth_Lib.Application.Abstractions;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using Auth_Lib.Errors;
using Auth_Lib.Validators;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Handler for creating a new user.
/// </summary>
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        ILogger<CreateUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _logger = logger;
    }

    public async Task<ErrorOr<UserDto>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
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

        // Create user
        var user = User.Create(
            email: request.Email,
            passwordHash: passwordHash,
            firstName: request.FirstName,
            lastName: request.LastName,
            createdBy: request.CreatedBy,
            displayName: request.DisplayName,
            phoneNumber: request.PhoneNumber,
            preferredLanguage: request.PreferredLanguage ?? "en",
            timeZone: request.TimeZone ?? "UTC");

        await _userRepository.CreateAsync(user, cancellationToken);

        // Assign roles if provided
        var roleNames = new List<string>();
        if (request.RoleIds != null && request.RoleIds.Count > 0)
        {
            foreach (var roleId in request.RoleIds)
            {
                var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
                if (role != null)
                {
                    var userRole = UserRole.Create(user.Id, roleId, request.CreatedBy);
                    await _roleRepository.AssignToUserAsync(userRole, cancellationToken);
                    roleNames.Add(role.Code);
                }
            }
        }

        _logger.LogInformation(
            "User created: {UserId} ({Email}) by {CreatedBy}",
            user.Id, user.Email, request.CreatedBy);

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
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            ModifiedAt = user.ModifiedAt,
            Roles = roleNames
        };
    }
}
