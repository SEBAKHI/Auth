using Auth.Application.Common;
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
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly IImageStorageService _imageStorage;

    public RemoveProfileImageCommandHandler(
        IUserRepository userRepository,
        AccountActionPolicy accountActionPolicy,
        IImageStorageService imageStorage)
    {
        _userRepository = userRepository;
        _accountActionPolicy = accountActionPolicy;
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

        // The account rules; one's own image always passes the second.
        var allowed = await _accountActionPolicy.EnsureAllowedAsync(
            new AccountActionRequest(request.ActingUserId, user.Id, AccountAction.ProfileImage), cancellationToken);
        if (allowed.IsError)
        {
            return allowed.Errors;
        }

        var oldKey = user.ProfileImageUrl;
        user.RemoveProfileImage(request.ActingUserId);
        await _userRepository.UpdateAsync(user, cancellationToken);

        await _imageStorage.DeleteImageAsync(oldKey, cancellationToken);

        return Result.Success;
    }
}
