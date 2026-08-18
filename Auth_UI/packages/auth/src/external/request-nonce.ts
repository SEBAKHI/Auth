import { api } from "@authsystem/api/client"

/**
 * Asks the server for a sign-in nonce and returns the plain value.
 *
 * The server keeps the matching half in an HttpOnly cookie, so presenting this
 * value at sign-in shows the token was minted for THIS browser. A value the
 * browser makes up proves nothing: the same request carries both the token and
 * the value it is checked against, so whoever holds a stolen token simply reads
 * the nonce out of it and sends the pair.
 *
 * Falls back to a locally generated value when the call fails — an older API
 * has no such endpoint, and a sign-in button that stops working the moment the
 * two halves are deployed out of order would be a worse failure than the weaker
 * check it replaces. The server decides whether the weaker value is acceptable
 * (the `ExternalAuth:RequireNonce` setting); this side never has to know.
 */
export async function requestExternalNonce(): Promise<string> {
  try {
    const { data } = await api.POST("/api/v1/Auth/external-nonce", {})
    if (data?.nonce) return data.nonce
  } catch {
    // Deliberately silent: falling back is the designed behaviour, not an error
    // worth showing someone who is only trying to sign in.
  }

  return crypto.randomUUID()
}
