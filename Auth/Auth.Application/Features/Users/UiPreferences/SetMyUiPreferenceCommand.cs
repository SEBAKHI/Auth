using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Stores or replaces one client display preference for the calling user.
/// </summary>
public record SetMyUiPreferenceCommand(Guid UserId, string Key, string Value)
    : IRequest<ErrorOr<Success>>;
