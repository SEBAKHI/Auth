using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Handler for <see cref="DeleteMyUiPreferenceCommand"/>.
/// </summary>
public class DeleteMyUiPreferenceCommandHandler
    : IRequestHandler<DeleteMyUiPreferenceCommand, ErrorOr<Success>>
{
    private readonly IUserUiPreferenceRepository _repository;

    public DeleteMyUiPreferenceCommandHandler(IUserUiPreferenceRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<Success>> Handle(
        DeleteMyUiPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        // Deleting a key the user does not hold is the same outcome they asked
        // for, so it is not an error.
        await _repository.DeleteAsync(request.UserId, request.Key, cancellationToken);
        return Result.Success;
    }
}
