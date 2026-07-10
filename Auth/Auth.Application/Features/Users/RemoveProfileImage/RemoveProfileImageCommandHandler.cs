using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.RemoveProfileImage;

/// <summary>Handler for <see cref="RemoveProfileImageCommand"/>.</summary>
public class RemoveProfileImageCommandHandler : IRequestHandler<RemoveProfileImageCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IImageStorageService _imageStorage;

    public RemoveProfileImageCommandHandler(
        IUserRepository userRepository,
        IImageStorageService imageStorage)
    {
        _userRepository = userRepository;
        _imageStorage = imageStorage;
    }

    public async Task<ErrorOr<Success>> Handle(
        RemoveProfileImageCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var oldKey = user.ProfileImageUrl;
        user.RemoveProfileImage(request.ActingUserId);
        await _userRepository.UpdateAsync(user, cancellationToken);

        await _imageStorage.DeleteImageAsync(oldKey, cancellationToken);

        return Result.Success;
    }
}
