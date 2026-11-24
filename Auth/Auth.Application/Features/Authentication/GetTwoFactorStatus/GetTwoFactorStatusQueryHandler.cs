using System.Text.Json;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Users.ResetUserTwoFactor;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetTwoFactorStatus;

/// <summary>
/// Handler for the two-factor status query: counts the recovery codes left on the
/// enabled factor, so the security page can say so — and warn while few are left,
/// before the last one goes with the phone. For an administrator it also says what
/// stands between them and no way back: a sign-in by passkey alone, and whether any
/// other administrator could reset their second factor.
/// </summary>
/// <remarks>
/// Both administrator answers come from the rules that act on them, never from a
/// copy: the passkey-only refusal's own <see cref="PasskeyOnlyAdministratorRule.AppliesToAsync"/>,
/// and the reset's own eligibility (<see cref="TwoFactorResetAdministrators"/>). An
/// account without platform authority reads neither: it holds no passkey-only
/// refusal, and no administrator's warning concerns it.
/// </remarks>
public class GetTwoFactorStatusQueryHandler : IRequestHandler<GetTwoFactorStatusQuery, ErrorOr<TwoFactorStatusResponse>>
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly EmailCodeFullProofRule _platformAuthority;
    private readonly PasskeyOnlyAdministratorRule _passkeyOnlyAdministrator;
    private readonly TwoFactorResetAdministrators _resetAdministrators;
    private readonly ILogger<GetTwoFactorStatusQueryHandler> _logger;

    public GetTwoFactorStatusQueryHandler(
        ITwoFactorStateStore twoFactorStateStore,
        EmailCodeFullProofRule platformAuthority,
        PasskeyOnlyAdministratorRule passkeyOnlyAdministrator,
        TwoFactorResetAdministrators resetAdministrators,
        ILogger<GetTwoFactorStatusQueryHandler> logger)
    {
        _twoFactorStateStore = twoFactorStateStore;
        _platformAuthority = platformAuthority;
        _passkeyOnlyAdministrator = passkeyOnlyAdministrator;
        _resetAdministrators = resetAdministrators;
        _logger = logger;
    }

    public async Task<ErrorOr<TwoFactorStatusResponse>> Handle(
        GetTwoFactorStatusQuery request,
        CancellationToken cancellationToken)
    {
        var codesRemaining = await RecoveryCodesRemainingAsync(request.UserId, cancellationToken);

        if (!await _platformAuthority.HoldsPlatformAuthorityAsync(request.UserId, cancellationToken))
        {
            return new TwoFactorStatusResponse(codesRemaining, SignsInWithPasskeyOnly: false, AnotherAdministratorCanReset: null);
        }

        return new TwoFactorStatusResponse(
            codesRemaining,
            SignsInWithPasskeyOnly: await _passkeyOnlyAdministrator.AppliesToAsync(request.UserId, cancellationToken),
            AnotherAdministratorCanReset: await _resetAdministrators.AnyAsync(request.UserId, cancellationToken));
    }

    /// <summary>The codes left on the enabled factor; null without one.</summary>
    private async Task<int?> RecoveryCodesRemainingAsync(Guid userId, CancellationToken cancellationToken)
    {
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(userId, cancellationToken);

        // A pending (not yet enabled) row has no codes that work.
        return snapshot is { IsEnabled: true } ? CountCodes(userId, snapshot.RecoveryCodes) : null;
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
