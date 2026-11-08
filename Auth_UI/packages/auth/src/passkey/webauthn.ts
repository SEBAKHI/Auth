/**
 * The one door to the WebAuthn helper (`@simplewebauthn/browser`, S20 T-C1).
 *
 * Nothing else imports the package — `webauthn-import.test.ts` fails on any other
 * import of it — and nothing here imports it at the top: it arrives by dynamic
 * import, after the page has seen `PublicKeyCredential` and the server has
 * answered passkey options with a 200. A sign-in page of a deployment that does
 * not run passkeys therefore never downloads it, and the bytes of every first
 * chunk stay as they were (login-payload.spec.ts).
 *
 * The helper is loaded BEFORE the click, never inside it: a browser grants a
 * passkey ceremony only to a call made while the click is still being handled
 * (transient user activation), so the click must find the helper and the
 * options already in hand and call it without awaiting anything first (T-C2).
 */
export type {
  AuthenticationResponseJSON,
  PublicKeyCredentialCreationOptionsJSON,
  PublicKeyCredentialRequestOptionsJSON,
  RegistrationResponseJSON,
} from "@simplewebauthn/browser"

/** The helper's functions, as the dynamic import yields them. */
export type WebAuthnHelper = typeof import("@simplewebauthn/browser")

let loading: Promise<WebAuthnHelper> | null = null

/**
 * Whether this browser has WebAuthn at all, read without loading anything. A
 * browser without it gets no passkey control, and no request for options.
 */
export function browserHasWebAuthn(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof (window as { PublicKeyCredential?: unknown }).PublicKeyCredential === "function"
  )
}

/**
 * The helper, loaded once per document. A failed load (a chunk gone after a
 * deployment, a dropped connection) is forgotten, so the next caller tries again.
 */
export function loadWebAuthn(): Promise<WebAuthnHelper> {
  loading ??= import("@simplewebauthn/browser").catch((error: unknown) => {
    loading = null
    throw error
  })
  return loading
}

/**
 * Whether a ceremony ended because it was cancelled: by the person (closing the
 * browser's dialog, or letting it time out — `NotAllowedError`), or by this page
 * starting another one or leaving (`AbortError`). Neither is a failure to show.
 */
export function isCeremonyCancelled(error: unknown): boolean {
  if (!error || typeof error !== "object") return false
  const { name, code, cause } = error as { name?: unknown; code?: unknown; cause?: unknown }
  if (code === "ERROR_CEREMONY_ABORTED") return true
  if (name === "AbortError" || name === "NotAllowedError") return true
  // The helper wraps the browser's error and keeps it as the cause.
  if (cause && typeof cause === "object") {
    const causeName = (cause as { name?: unknown }).name
    return causeName === "AbortError" || causeName === "NotAllowedError"
  }
  return false
}

/**
 * Whether a registration was refused because this device already holds a passkey
 * of the account (`InvalidStateError`: a credential the options excluded).
 */
export function isAlreadyOnThisDevice(error: unknown): boolean {
  if (!error || typeof error !== "object") return false
  const { name, code, cause } = error as { name?: unknown; code?: unknown; cause?: unknown }
  if (code === "ERROR_AUTHENTICATOR_PREVIOUSLY_REGISTERED") return true
  if (name === "InvalidStateError") return true
  return Boolean(
    cause && typeof cause === "object" && (cause as { name?: unknown }).name === "InvalidStateError"
  )
}
