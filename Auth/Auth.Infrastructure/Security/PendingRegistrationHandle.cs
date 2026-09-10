using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Keyed-digest implementation of <see cref="IPendingRegistrationHandle"/>.
/// </summary>
/// <remarks>
/// Reuses the platform's HMAC key, as the OTP hasher and the identifier hasher
/// do, and keeps its use apart from theirs with a label no other caller of that
/// key writes: <c>pending-registration-handle:v1:</c>. Rows store the handle
/// they were last issued a code under, and the start step re-keys it with every
/// code, so a rotation of the platform key changes what a NEW start derives
/// while a row's stored handle keeps matching until its next code: the handle
/// a client is answered with is always the stored one, never the derived one.
/// </remarks>
public sealed class PendingRegistrationHandle : IPendingRegistrationHandle
{
    /// <summary>
    /// Distinct from <c>otp:v1:</c> (codes) and <c>email:</c> (tombstones), so a
    /// handle can never be replayed as either.
    /// </summary>
    private const string Label = "pending-registration-handle:v1:";

    private readonly IRefreshTokenKeyService _keyService;

    public PendingRegistrationHandle(IRefreshTokenKeyService keyService)
    {
        _keyService = keyService;
    }

    /// <inheritdoc />
    public string For(string normalizedEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        return _keyService.ComputeTokenHash(Label + normalizedEmail);
    }
}
