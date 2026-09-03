using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared helper methods for authentication handlers.
/// </summary>
internal static class AuthenticationHelper
{
    /// <summary>
    /// How far back a successful sign-in from an address still counts as "the
    /// owner's own network". Thirty days covers a monthly-travel cadence without
    /// keeping a coffee-shop address familiar forever.
    /// </summary>
    internal static readonly TimeSpan FamiliarSourceLookback = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether the caller is a familiar source for this account: an address it
    /// has signed in from within <see cref="FamiliarSourceLookback"/>, or a
    /// device it holds a session on. A lock raised by strangers' wrong passwords
    /// exists to stop the strangers, not the owner — so a familiar source may
    /// still attempt, bounded by its own per-source failure ceiling.
    /// </summary>
    internal static Task<bool> IsFamiliarSourceAsync(
        ILoginAttemptRepository loginAttempts,
        Guid userId,
        string? ipAddress,
        string? deviceId,
        CancellationToken cancellationToken)
        => loginAttempts.HasSucceededFromAsync(userId, ipAddress, deviceId, FamiliarSourceLookback, cancellationToken);

    /// <summary>
    /// Checks whether the user's account status allows login.
    /// Returns an error for Inactive, Locked, or Pending accounts.
    /// </summary>
    internal static ErrorOr<Success> CheckAccountStatus(User user)
    {
        return user.Status switch
        {
            UserStatus.Inactive => UserErrors.AccountInactive,
            UserStatus.Pending => UserErrors.AccountPending,
            _ => Result.Success
        };
    }

}
