/**
 * The Google sign-in's authorization-code flow (S02 PR D), the parts both the sign-in page and
 * the callback page share. No Google script runs on our pages: the button opens a popup to
 * Google's authorization page, Google sends the person back to /google-callback.html with a
 * one-time code, that page hands the code to the sign-in page over a same-origin channel, and
 * the API exchanges it (client secret + PKCE) for the ID token the sign-in validates as before.
 *
 * This module imports nothing, so the callback page's bundle stays this file and its own.
 */

/** Google's authorization endpoint (OpenID Connect discovery, `authorization_endpoint`). */
export const GOOGLE_AUTHORIZE_URL = "https://accounts.google.com/o/oauth2/v2/auth"

/**
 * The page Google sends the person back to, on each app's own origin. Every OAuth client at
 * Google lists `<app origin>/google-callback.html` among its authorized redirect URIs, and the
 * API builds the same address from the verified Origin (AuthController.GoogleCallbackPath).
 */
export const GOOGLE_CALLBACK_PATH = "/google-callback.html"

/** The same-origin channel the callback page answers on. */
export const GOOGLE_CHANNEL = "auth.google-sign-in"

/** What the callback page tells the sign-in page, once. */
export type GoogleCallbackMessage =
  | { type: "google-code"; state: string; code: string }
  | { type: "google-error"; state: string; error: string }

/** RFC 4648 §5 base64url, without padding (RFC 7636 §3). */
export function base64Url(bytes: Uint8Array): string {
  let binary = ""
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "")
}

/**
 * A fresh random value: 32 bytes, base64url, 43 characters. Used for `state` and for the PKCE
 * verifier, whose 43 characters are RFC 7636 §4.1's minimum and its recommended 256 bits.
 */
export function randomBase64Url(): string {
  return base64Url(crypto.getRandomValues(new Uint8Array(32)))
}

/** The PKCE S256 challenge of a verifier: base64url(SHA-256(ASCII(verifier))) (RFC 7636 §4.2). */
export async function pkceChallenge(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))
  return base64Url(new Uint8Array(digest))
}

/** Maps an i18n tag ("ar", "zh-CN") to the ISO 639-1 code Google's `hl` takes. */
export function googleLocale(language: string): string {
  return language.split("-")[0].toLowerCase()
}

export interface AuthorizeRequest {
  clientId: string
  /** `<this app's origin>/google-callback.html`. */
  redirectUri: string
  state: string
  /** The server-issued nonce Google seals into the ID token. */
  nonce: string
  codeChallenge: string
  /** Google's page language (`hl`). */
  locale: string
}

/**
 * Google's authorization URL for one sign-in, with exactly these parameters and nothing else:
 * no `access_type=offline` (no refresh token is wanted) and no `include_granted_scopes`.
 * `prompt=select_account` makes Google always show its account chooser, so the person picks
 * the account rather than being signed in as whoever Google last saw.
 */
export function buildAuthorizeUrl(request: AuthorizeRequest): string {
  const url = new URL(GOOGLE_AUTHORIZE_URL)
  url.search = new URLSearchParams({
    client_id: request.clientId,
    redirect_uri: request.redirectUri,
    response_type: "code",
    scope: "openid email profile",
    state: request.state,
    nonce: request.nonce,
    code_challenge: request.codeChallenge,
    code_challenge_method: "S256",
    prompt: "select_account",
    hl: request.locale,
  }).toString()
  return url.toString()
}

/** Whether a value received on the channel or by postMessage is the callback page's message. */
export function isGoogleCallbackMessage(data: unknown): data is GoogleCallbackMessage {
  if (typeof data !== "object" || data === null) return false
  const message = data as Record<string, unknown>
  if (typeof message.state !== "string" || message.state.length === 0) return false
  if (message.type === "google-code") return typeof message.code === "string" && message.code.length > 0
  if (message.type === "google-error") return typeof message.error === "string"
  return false
}

/**
 * What Google appended to the callback address, as the message to send: the code, or Google's
 * error (RFC 6749 §4.1.2.1), each with the `state` it echoed. Null when it carries neither, or
 * no state (nothing to match it against).
 */
export function readCallback(search: string): GoogleCallbackMessage | null {
  const params = new URLSearchParams(search)
  const state = params.get("state")
  if (!state) return null
  const code = params.get("code")
  if (code) return { type: "google-code", state, code }
  const error = params.get("error")
  if (error !== null) return { type: "google-error", state, error }
  return null
}
