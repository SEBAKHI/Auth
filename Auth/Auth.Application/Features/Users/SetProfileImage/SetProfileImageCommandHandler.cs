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
    private readonly IUploadedImageRepository _uploadedImages;

    public SetProfileImageCommandHandler(
        IUserRepository userRepository,
        IImageStorageService imageStorage,
        IUploadedImageRepository uploadedImages)
    {
        _userRepository = userRepository;
        _imageStorage = imageStorage;
        _uploadedImages = uploadedImages;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetProfileImageCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Claim the key before writing it. This used to accept whatever key the
        // client sent, and the cleanup below deletes the key being replaced — so
        // pointing at somebody else's image and then changing your mind deleted
        // their file. Possession of a key was the whole of the claim to it.
        if (!await _uploadedImages.TryAttachAsync(
                request.ImageKey, request.ActingUserId, cancellationToken))
        {
            return ImageErrors.NotAvailable;
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
