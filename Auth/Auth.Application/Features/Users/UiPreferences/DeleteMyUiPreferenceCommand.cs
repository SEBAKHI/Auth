using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Removes one client display preference belonging to the calling user.
/// </summary>
public record DeleteMyUiPreferenceCommand(Guid UserId, string Key) : IRequest<ErrorOr<Success>>;
