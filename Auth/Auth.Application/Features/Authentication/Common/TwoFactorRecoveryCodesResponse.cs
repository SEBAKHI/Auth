namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// A new set of recovery codes, shown once: only their hashes are stored.
/// </summary>
/// <param name="RecoveryCodes">The codes, in plain text, for the user to keep.</param>
public record TwoFactorRecoveryCodesResponse(string[] RecoveryCodes)
{
    // The codes are secrets; the synthesized ToString would print them into any
    // log line or assertion message the response reaches.
    public override string ToString() =>
        $"TwoFactorRecoveryCodesResponse {{ Count = {RecoveryCodes.Length} }}";
}
