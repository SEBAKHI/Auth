using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared helper methods for authentication handlers.
/// </summary>
internal static class AuthenticationHelper
{
    /// <summary>
    /// Checks whether the user's account status allows login.
    /// Returns an error for Inactive, Locked, or Pending accounts.
    /// </summary>
    internal static ErrorOr<Success> CheckAccountStatus(User user)
    {
        return user.Status switch
        {
            UserStatus.Inactive => UserErrors.AccountInactive,
            UserStatus.Locked => UserErrors.AccountLocked,
            UserStatus.Pending => UserErrors.AccountPending,
            _ => Result.Success
        };
    }

    /// <summary>
    /// Builds a combined device info string from the user agent and device ID.
    /// Returns null if both values are empty.
    /// </summary>
    internal static string? BuildDeviceInfo(string? userAgent, string? deviceId)
    {
        if (string.IsNullOrEmpty(userAgent) && string.IsNullOrEmpty(deviceId))
            return null;

        if (string.IsNullOrEmpty(deviceId))
            return userAgent;

        if (string.IsNullOrEmpty(userAgent))
            return $"DeviceId: {deviceId}";

        return $"{userAgent} | DeviceId: {deviceId}";
    }
}
