namespace Auth.Domain.Enums;

/// <summary>
/// One change to the HMAC keyring (S11, KR3). A keyring change is confirmed with the
/// step-up of <see cref="SecretOperation.GenerateHmacKey"/> (or
/// <see cref="SecretOperation.ImportHmacKey"/> for <see cref="StageImport"/>), and the
/// confirmation is bound to the action and its target through its payload digest, so
/// an approval for one action is never spent on another, without a new operation
/// member (the operation column is pinned to six values by a CHECK constraint).
/// </summary>
/// <remarks>
/// The values are explicit and never renumbered: they travel in requests, in the
/// payload digest's text (by name) and in the audit trail.
/// </remarks>
public enum KeyringAction
{
    /// <summary>Generate a new key and stage it as next: it verifies, it does not issue yet.</summary>
    Stage = 1,

    /// <summary>Stage a key the administrator supplies.</summary>
    StageImport = 2,

    /// <summary>Make the staged key current; the old current keeps verifying through its window.</summary>
    Promote = 3,

    /// <summary>
    /// Replace the current key at once after a leak: every existing key is marked
    /// compromised and stops verifying, except for webhook keys, which keep their grace.
    /// </summary>
    PromoteEmergency = 4,

    /// <summary>Remove the staged key before it is promoted.</summary>
    DiscardNext = 5,

    /// <summary>Make a previous key current again (the rollback of a promote).</summary>
    Reinstate = 6,

    /// <summary>Remove a previous key from the ring for good.</summary>
    Retire = 7
}
