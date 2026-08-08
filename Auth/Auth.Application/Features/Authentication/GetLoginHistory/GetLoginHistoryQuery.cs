using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetLoginHistory;

/// <summary>
/// Query for the user's own recent sign-in attempts, successful and failed.
///
/// The companion to the session list, not a filter of it: the session list
/// answers "who is signed in right now", this answers "what has been tried
/// against my account". Merging them would give one surface two lengths and two
/// interaction models.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Take">How many entries to return, most recent first.</param>
public record GetLoginHistoryQuery(
    Guid UserId,
    int Take = 20) : IRequest<ErrorOr<IReadOnlyList<LoginAttemptDto>>>;
