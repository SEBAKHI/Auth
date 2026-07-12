/**
 * Minimal, dependency-free JWT helpers.
 *
 * We only ever DECODE the access token on the client to read non-sensitive
 * claims (expiry, permissions) for UI gating. The API remains the source of
 * truth and re-validates the signature on every request.
 */

export interface JwtClaims {
  sub?: string
  email?: string
  name?: string
  jti?: string
  exp?: number
  iat?: number
  roles?: string | string[]
  permissions?: string | string[]
  [claim: string]: unknown
}

/** Decode the payload of a JWT. Returns null for malformed tokens. */
export function decodeJwt(token: string): JwtClaims | null {
  try {
    const payload = token.split(".")[1]
    if (!payload) return null

    const base64 = payload.replace(/-/g, "+").replace(/_/g, "/")
    const binary = atob(base64)

    // Decode as UTF-8 so non-ASCII claim values survive.
    const json = decodeURIComponent(
      Array.from(binary)
        .map((char) => "%" + char.charCodeAt(0).toString(16).padStart(2, "0"))
        .join("")
    )

    return JSON.parse(json) as JwtClaims
  } catch {
    return null
  }
}

/** Normalize a single-or-multi-valued string claim into an array. */
export function claimToArray(value: string | string[] | undefined): string[] {
  if (!value) return []
  return Array.isArray(value) ? value : [value]
}

/** True when the token is at/over its expiry (minus a small clock-skew buffer). */
export function isTokenExpired(claims: JwtClaims, skewSeconds = 30): boolean {
  if (typeof claims.exp !== "number") return false
  return Date.now() >= (claims.exp - skewSeconds) * 1000
}
