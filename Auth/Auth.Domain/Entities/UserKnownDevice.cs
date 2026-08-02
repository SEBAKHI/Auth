using System.Security.Cryptography;
using System.Text;
using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// A device a user has signed in from before.
///
/// Recognition only. The signature is client-influenced and therefore
/// spoofable, so it must never be read as an authorization input — its single
/// job is to decide whether a sign-in is worth telling the user about. An
/// attacker able to forge it already has the victim's browser storage.
/// </summary>
public class UserKnownDevice : EntityBase
{
    /// <summary>Gets the owning user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the SHA-256 signature that identifies the device.</summary>
    public string DeviceHash { get; private set; } = string.Empty;

    /// <summary>Gets the human label for the alert, e.g. "Chrome on Windows".</summary>
    public string? DeviceName { get; private set; }

    /// <summary>Gets when this device was first seen.</summary>
    public DateTime FirstSeenAt { get; private set; }

    /// <summary>Gets when this device was last seen.</summary>
    public DateTime LastSeenAt { get; private set; }

    /// <summary>Gets when an alert was last sent for this device, if ever.</summary>
    public DateTime? LastAlertSentAt { get; private set; }

    private UserKnownDevice()
    {
    }

    public UserKnownDevice(
        Guid id,
        Guid userId,
        string deviceHash,
        string? deviceName,
        DateTime firstSeenAt,
        DateTime lastSeenAt,
        DateTime? lastAlertSentAt) : base(id)
    {
        UserId = userId;
        DeviceHash = deviceHash;
        DeviceName = deviceName;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        LastAlertSentAt = lastAlertSentAt;
    }

    /// <summary>Creates a first sighting of a device.</summary>
    public static UserKnownDevice Create(
        Guid userId,
        string deviceHash,
        string? deviceName,
        DateTime? alertSentAt = null)
    {
        var now = DateTime.UtcNow;
        return new UserKnownDevice(Guid.NewGuid(), userId, deviceHash, deviceName, now, now, alertSentAt);
    }

    /// <summary>
    /// Derives the signature for a device.
    ///
    /// Browser and OS *families* only — versions are excluded because browsers
    /// update themselves, and an alert on every update trains the user to
    /// ignore the alert. The client-supplied device id is what makes two
    /// machines running the same browser distinguishable; without it the
    /// signature is coarse and under-reports, which is the safer direction to
    /// be wrong in for a notification.
    /// </summary>
    public static string ComputeHash(string? deviceId, string? browser, string? os)
    {
        var material = string.Join(
            '|',
            deviceId?.Trim() ?? string.Empty,
            browser ?? string.Empty,
            os ?? string.Empty);

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>Records another sign-in from this device.</summary>
    public void Touch(string? deviceName)
    {
        LastSeenAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            DeviceName = deviceName;
        }
    }

    /// <summary>Stamps that the user has been told about this device.</summary>
    public void MarkAlertSent() => LastAlertSentAt = DateTime.UtcNow;
}
