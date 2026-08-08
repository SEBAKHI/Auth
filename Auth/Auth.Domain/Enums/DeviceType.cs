using System.Text.Json.Serialization;

namespace Auth.Domain.Enums;

/// <summary>
/// The physical form factor a sign-in came from.
///
/// Distinct from the device *identity* in <see cref="Entities.UserKnownDevice"/>:
/// this says what kind of thing it is, not which one. Both live on a session row
/// because they answer different questions — "is this a phone?" and "have I seen
/// this browser before?".
///
/// The names are lowercase on the wire and in the database because both ends
/// already speak that vocabulary: the <c>DeviceType</c> column's documented
/// values, and the <c>profile.deviceType.*</c> translation keys the sessions view
/// indexes by. Changing either to PascalCase would break a lookup rather than a
/// compile.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceType>))]
public enum DeviceType
{
    /// <summary>The user agent named nothing recognisable, or there was none.</summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>A desktop or laptop computer. The fallback for any agent that parses but names no form factor.</summary>
    [JsonStringEnumMemberName("desktop")]
    Desktop,

    /// <summary>A phone.</summary>
    [JsonStringEnumMemberName("mobile")]
    Mobile,

    /// <summary>A tablet. Checked before mobile: an Android tablet's agent matches both.</summary>
    [JsonStringEnumMemberName("tablet")]
    Tablet
}
