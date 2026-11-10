import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import type { Schemas } from "@authsystem/api/types"
import { signalAcceptedPasskeys } from "@authsystem/auth/passkey/passkey-signal"

type PasskeyList = Schemas["PasskeyListDto"]

/**
 * The account's passkeys, under "me" so that a change to the account reads them
 * again. Null while passkeys do not run here: the list answers 404, and the card
 * is not shown at all.
 */
export const PASSKEYS_QUERY_KEY = ["me", "passkeys"] as const

/**
 * The longest name the server stores, in code points (`UserPasskey.MaxNameCodePoints`;
 * passkey-list.test.ts reads the C# constant). The server checks it after the
 * ceremony; the add dialog asks first, so no fingerprint is spent on a name that
 * would be refused.
 */
export const PASSKEY_NAME_MAX_CODE_POINTS = 64

/**
 * Reads the list, and tells the browser which passkeys the account still has
 * (D-S20-7): here, on a fresh answer from the server, never from the cache. A read
 * that a newer one replaced tells it nothing: its list may lack a passkey just
 * added, and the browser would hide that passkey until the next read.
 */
async function fetchPasskeys(signal?: AbortSignal): Promise<PasskeyList | null> {
  const { data, error, response } = await api.GET("/api/v1/auth/passkeys", { signal })
  if (response.status === 404) return null
  if (error || !data) throw error ?? new Error("Passkeys failed to load")
  if (!signal?.aborted) {
    signalAcceptedPasskeys({
      rpId: data.rpId,
      userHandle: data.userHandle,
      credentialIds: data.passkeys.map((passkey) => passkey.credentialId),
    })
  }
  return data
}

/** The account's passkeys, or null while passkeys do not run here. */
export function usePasskeys() {
  return useQuery({
    queryKey: PASSKEYS_QUERY_KEY,
    queryFn: ({ signal }) => fetchPasskeys(signal),
  })
}
