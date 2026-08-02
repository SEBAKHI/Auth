using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Reads every client display preference belonging to the calling user, as a
/// key → value map. Self-service: the caller can only ever read their own.
/// </summary>
public record GetMyUiPreferencesQuery(Guid UserId)
    : IRequest<ErrorOr<IReadOnlyDictionary<string, string>>>;
