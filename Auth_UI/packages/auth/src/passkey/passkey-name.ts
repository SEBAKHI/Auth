/**
 * The longest name the server stores for a passkey, in code points
 * (`UserPasskey.MaxNameCodePoints`; the account package's passkey-list.test.ts reads the C#
 * constant). The server checks it after the ceremony; the addition asks first, so no fingerprint
 * is spent on a name that would be refused.
 */
export const PASSKEY_NAME_MAX_CODE_POINTS = 64
