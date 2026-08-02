using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Handler for <see cref="GetMyUiPreferencesQuery"/>.
/// </summary>
public class GetMyUiPreferencesQueryHandler
    : IRequestHandler<GetMyUiPreferencesQuery, ErrorOr<IReadOnlyDictionary<string, string>>>
{
    private readonly IUserUiPreferenceRepository _repository;

    public GetMyUiPreferencesQueryHandler(IUserUiPreferenceRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<IReadOnlyDictionary<string, string>>> Handle(
        GetMyUiPreferencesQuery request,
        CancellationToken cancellationToken)
    {
        var preferences = await _repository.GetAllForUserAsync(request.UserId, cancellationToken);

        // A user with no stored preferences is the normal first-visit case, not
        // an error: the client falls back to its own defaults.
        return preferences.ToDictionary(p => p.Key, p => p.Value);
    }
}
