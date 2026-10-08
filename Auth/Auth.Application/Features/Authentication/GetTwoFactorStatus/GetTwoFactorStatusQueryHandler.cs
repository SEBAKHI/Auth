using System.Text.Json;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetTwoFactorStatus;

/// <summary>
/// Handler for the two-factor status query: counts the recovery codes left on the
/// enabled factor, so the security page can say so — and warn while few are left,
/// before the last one goes with the phone.
/// </summary>
public class GetTwoFactorStatusQueryHandler : IRequestHandler<GetTwoFactorStatusQuery, ErrorOr<TwoFactorStatusResponse>>
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ILogger<GetTwoFactorStatusQueryHandler> _logger;

    public GetTwoFactorStatusQueryHandler(
        ITwoFactorStateStore twoFactorStateStore,
        ILogger<GetTwoFactorStatusQueryHandler> logger)
    {
        _twoFactorStateStore = twoFactorStateStore;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorStatusResponse>> Handle(
        GetTwoFactorStatusQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(request.UserId, cancellationToken);

        // A pending (not yet enabled) row has no codes that work.
        if (snapshot is not { IsEnabled: true })
        {
            return new TwoFactorStatusResponse(null);
        }

        return new TwoFactorStatusResponse(CountCodes(request.UserId, snapshot.RecoveryCodes));
    }

    /// <summary>
    /// The length of the stored array of hashes: one per unused code, since a code
    /// is removed from the array when it is spent.
    /// </summary>
    private int CountCodes(Guid userId, string? storedCodes)
    {
        if (string.IsNullOrWhiteSpace(storedCodes))
        {
            return 0;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(storedCodes)?.Count ?? 0;
        }
        catch (JsonException ex)
        {
            // An unreadable set lets no code through at sign-in either, so none are
            // left: zero shows the warning that leads to a new set.
            _logger.LogWarning(ex, "The stored recovery codes of user {UserId} could not be read; reporting none left", userId);
            return 0;
        }
    }
}
