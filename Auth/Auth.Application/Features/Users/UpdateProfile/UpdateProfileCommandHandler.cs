using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UpdateProfile;

/// <summary>
/// Handler for the update profile command (self-service).
/// </summary>
public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UpdateProfileCommandHandler> _logger;

    public UpdateProfileCommandHandler(
        IUserRepository userRepository,
        ILogger<UpdateProfileCommandHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<UserDto>> Handle(
        UpdateProfileCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Apply updates only for fields that are provided
        var firstName = request.FirstName ?? user.FirstName;
        var lastName = request.LastName ?? user.LastName;
        var displayName = request.DisplayName ?? user.DisplayName;
        var phoneNumber = request.PhoneNumber ?? user.PhoneNumber;
        var preferredLanguage = request.PreferredLanguage ?? user.PreferredLanguage;
        var timeZone = request.TimeZone ?? user.TimeZone;
        var theme = request.Theme ?? user.Theme;

        user.UpdateProfile(
            firstName,
            lastName,
            displayName,
            phoneNumber,
            preferredLanguage,
            timeZone,
            theme,
            request.UserId);

        await _userRepository.UpdateAsync(user, cancellationToken);

        _logger.LogInformation(
            "User {UserId} updated their profile",
            request.UserId);

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            PreferredLanguage = user.PreferredLanguage,
            TimeZone = user.TimeZone,
            Theme = user.Theme,
            Status = user.Status,
            EmailConfirmed = user.EmailConfirmed,
            PhoneConfirmed = user.PhoneConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        };
    }
}
