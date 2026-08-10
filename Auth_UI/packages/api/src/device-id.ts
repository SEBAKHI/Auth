const STORAGE_KEY = "auth.deviceId"

/**
 * Transport header carrying {@link getDeviceId}.
 *
 * The value is a client fact like the IP and the user agent, so it travels the
 * same way they do. It used to be a field in the body of each request that
 * creates a session, which meant every such endpoint had to remember to include
 * it — and one did not.
 */
export const DEVICE_ID_HEADER = "X-Device-Id"

/**
 * A stable per-browser identifier, sent with every sign-in.
 *
 * Its only job is to tell one device from another when deciding whether a
 * sign-in is worth emailing the account owner about. Without it, two different
 * machines running the same browser and OS look identical to the server and
 * the second one never raises an alert.
 *
 * It is NOT a security control and the server treats it as none: it is
 * client-supplied and therefore forgeable. Anyone who can read this value has
 * the victim's browser storage and has already won.
 *
 * Clearing site data regenerates it, which costs one extra alert — the
 * server's per-user floor is what keeps that from becoming a stream.
 */
export function getDeviceId(): string | undefined {
  if (typeof window === "undefined") return undefined

  try {
    const existing = window.localStorage.getItem(STORAGE_KEY)
    if (existing) return existing

    const generated = crypto.randomUUID()
    window.localStorage.setItem(STORAGE_KEY, generated)
    return generated
  } catch {
    // Private mode or a blocked store: sign-in must not depend on this.
    return undefined
  }
}
