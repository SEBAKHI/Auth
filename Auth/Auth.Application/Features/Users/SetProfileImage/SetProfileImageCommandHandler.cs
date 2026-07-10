using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.SetProfileImage;

/// <summary>Handler for <see cref="SetProfileImageCommand"/>.</summary>
public class SetProfileImageCommandHandler : IRequestHandler<SetProfileImageCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IImageStorageService _imageStorage;

    public SetProfileImageCommandHandler(
        IUserRepository userRepository,
        IImageStorageService imageStorage)
    {
        _userRepository = userRepository;
        _imageStorage = imageStorage;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetProfileImageCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var oldKey = user.ProfileImageUrl;
        user.SetProfileImage(request.ImageKey, request.ActingUserId);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // Best-effort cleanup of the replaced file (no-op for unchanged/external URLs).
        if (!string.IsNullOrEmpty(oldKey) && oldKey != request.ImageKey)
        {
            await _imageStorage.DeleteImageAsync(oldKey, cancellationToken);
        }

        return Result.Success;
    }
}
