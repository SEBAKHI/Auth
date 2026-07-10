using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.SetProfileImage;

/// <summary>
/// Sets a user's profile image to an already-uploaded storage key.
/// <paramref name="UserId"/> is the target; <paramref name="ActingUserId"/> is the caller (audit).
/// </summary>
public record SetProfileImageCommand(Guid UserId, string ImageKey, Guid ActingUserId)
    : IRequest<ErrorOr<Success>>;
