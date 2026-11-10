import { api } from "@authsystem/api/client"

/**
 * The sign-in page's one question after the address (OI-280, D-S20-10): does
 * this address have a live passkey here? The server answers the same for an
 * address without a passkey and for an unknown one, and nothing else; the
 * owner accepted that a stranger can ask (at the server's own rate).
 *
 * - `passkey` — the address has a passkey: the passkey screen;
 * - `none` — it has none, or there is no such account: the next step as before;
 * - `off` — passkeys do not run here (the route's 404): the next step as
 *   before, and the page stops asking for the rest of its life;
 * - `unknown` — anything else (network, 5xx, 429, 400, or no answer within
 *   {@link PASSKEY_LOOKUP_TIMEOUT_MS}): the next step as before, silently.
 *
 * Nothing about the answer is kept anywhere (D-215-4): the page acts on it once.
 */
export type PasskeyLookup = "passkey" | "none" | "off" | "unknown"

/**
 * How long the page waits for the answer before going on as it would without
 * one (D7): the code, in code mode, waits on it.
 */
export const PASSKEY_LOOKUP_TIMEOUT_MS = 3_000

async function ask(email: string, signal: AbortSignal): Promise<PasskeyLookup> {
  try {
    const { data, response } = await api.POST("/api/v1/auth/passkeys/lookup", {
      body: { email },
      signal,
    })
    // Only this 404 means passkeys do not run here (as for their options, T-C4).
    if (response.status === 404) return "off"
    if (response.status === 200 && typeof data?.passkey === "boolean") {
      return data.passkey ? "passkey" : "none"
    }
    return "unknown"
  } catch {
    // The network, or the request was given up: the sign-in goes on as it would have.
    return "unknown"
  }
}

export async function lookUpPasskey(
  email: string,
  { timeoutMs = PASSKEY_LOOKUP_TIMEOUT_MS }: { timeoutMs?: number } = {}
): Promise<PasskeyLookup> {
  const controller = new AbortController()
  let timer: ReturnType<typeof setTimeout> | undefined
  // The page goes on at the deadline whatever the transport does with the abort.
  const deadline = new Promise<PasskeyLookup>((resolve) => {
    timer = setTimeout(() => {
      controller.abort()
      resolve("unknown")
    }, timeoutMs)
  })
  try {
    return await Promise.race([ask(email, controller.signal), deadline])
  } finally {
    clearTimeout(timer)
  }
}
