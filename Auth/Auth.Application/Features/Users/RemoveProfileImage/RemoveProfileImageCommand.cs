using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.RemoveProfileImage;

/// <summary>Clears a user's profile image (and best-effort deletes the stored file).</summary>
public record RemoveProfileImageCommand(Guid UserId, Guid ActingUserId)
    : IRequest<ErrorOr<Success>>;
