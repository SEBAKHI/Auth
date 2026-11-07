using System.Buffers.Text;
using System.Text.Json;
using Auth.Domain.Entities;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// Who a sign-in response says it is (S20 PR B, PK8): the credential id (<c>rawId</c>) and the
/// account's WebAuthn user handle (<c>response.userHandle</c>), read in this code, before and
/// outside the WebAuthn library, so the row can be found and the handle compared explicitly.
/// </summary>
/// <remarks>
/// Strict: <c>rawId</c> must be base64url of 1 to 1023 bytes, and a <c>userHandle</c>, when
/// present, base64url of at most 64 bytes (WebAuthn L3 §5.4.3). Anything else does not read,
/// and the sign-in is refused as any malformed answer is. Whether a handle is present at all is
/// the caller's rule: a discoverable credential always answers one.
/// </remarks>
public static class PasskeyAssertionIdentity
{
    /// <summary>The longest user handle WebAuthn allows, in bytes.</summary>
    public const int MaxUserHandleBytes = 64;

    /// <summary>Reads the credential id and the user handle of a sign-in response.</summary>
    /// <param name="responseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
    /// <param name="credentialId">The credential id, when it reads.</param>
    /// <param name="userHandle">The user handle; null when the response carries none.</param>
    /// <returns>Whether both read.</returns>
    public static bool TryRead(string responseJson, out byte[] credentialId, out byte[]? userHandle)
    {
        credentialId = [];
        userHandle = null;

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("rawId", out var rawId)
                || rawId.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var id = Base64Url.DecodeFromChars(rawId.GetString());
            if (id.Length is 0 or > UserPasskey.MaxCredentialIdBytes)
            {
                return false;
            }

            byte[]? handle = null;
            if (response.TryGetProperty("userHandle", out var userHandleElement)
                && userHandleElement.ValueKind != JsonValueKind.Null)
            {
                if (userHandleElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                handle = Base64Url.DecodeFromChars(userHandleElement.GetString());
                if (handle.Length is 0 or > MaxUserHandleBytes)
                {
                    return false;
                }
            }

            credentialId = id;
            userHandle = handle;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return false;
        }
    }
}
