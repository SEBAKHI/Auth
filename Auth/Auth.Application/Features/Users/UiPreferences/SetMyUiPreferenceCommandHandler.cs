using System.Text.Json;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Handler for <see cref="SetMyUiPreferenceCommand"/>.
/// </summary>
public class SetMyUiPreferenceCommandHandler
    : IRequestHandler<SetMyUiPreferenceCommand, ErrorOr<Success>>
{
    private readonly IUserUiPreferenceRepository _repository;

    public SetMyUiPreferenceCommandHandler(IUserUiPreferenceRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetMyUiPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Value.Length > UserUiPreference.MaxValueLength)
        {
            return UiPreferenceErrors.ValueTooLarge;
        }

        // The server never interprets the value, but it does insist it is JSON:
        // that keeps the column from becoming a dumping ground for arbitrary
        // text and bounds what a later reader has to defend against.
        if (!IsJson(request.Value))
        {
            return UiPreferenceErrors.ValueNotJson;
        }

        var existing = await _repository.GetAllForUserAsync(request.UserId, cancellationToken);
        var isNewKey = !existing.Any(p => p.Key == request.Key);

        // Only a new key can push the user over the ceiling; replacing a value
        // must keep working even for a user already at the limit.
        if (isNewKey && existing.Count >= UserUiPreference.MaxKeysPerUser)
        {
            return UiPreferenceErrors.TooManyKeys;
        }

        var preference = UserUiPreference.Create(request.UserId, request.Key, request.Value);
        await _repository.UpsertAsync(preference, cancellationToken);

        return Result.Success;
    }

    private static bool IsJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
