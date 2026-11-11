using System.Text;

namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request for setting a secret value.
/// </summary>
public record SetSecretRequest
{
    /// <summary>
    /// The secret value to store.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Names the member without its value: the framework writes an action's arguments into its
    /// Trace log, and this carries a secret (the SMTP password, Google's client secret, a custom
    /// secret).
    /// </summary>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Value = [redacted]");
        return true;
    }
}
