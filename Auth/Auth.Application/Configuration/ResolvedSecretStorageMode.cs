namespace Auth.Application.Configuration;

/// <summary>
/// The secret storage mode this process actually runs in, as startup resolved it,
/// which is not always the configured one: Development without a Data Protection
/// certificate falls back to PlainText (Program.cs, ResolveStorageMode). The keyring
/// refuses on this one, because a ring saved to an encrypted file that a PlainText
/// process never reads would be lost at the next start.
/// </summary>
/// <param name="Name">The mode's name, for messages.</param>
/// <param name="IsPlainText">Whether secrets live in plain settings files, with no encrypted file.</param>
public sealed record ResolvedSecretStorageMode(string Name, bool IsPlainText);
