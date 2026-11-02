import { API_BASE_URL } from "@authsystem/api/env"
import type { Schemas } from "@authsystem/api/types"
import i18n from "@authsystem/i18n"

/*
 * The account bar's two reads, with `fetch` and not the app's API client. That client is
 * the accounts app's session: its import joins the session's cross-tab sync, and a request
 * through it refreshes the accounts app's access token first, which from a frame inside
 * another application would rotate the person's refresh token behind the backs of their
 * accounts tabs. These two reads need no token: the bar's is authenticated by the single
 * sign-on cookie alone, and the branding is public. `embed-imports.test.ts` holds the
 * frames off that client.
 */

export type AccountBar = Schemas["AccountBarDto"]
export type PlatformBranding = Schemas["PlatformBrandingDto"]

/** The address of the account bar read; the API client leaves it anonymous too. */
export const ACCOUNT_BAR_PATH = "/api/v1/Auth/account-bar"

/** What the bar shows: the person, or null when this browser has no single sign-on session. */
export async function fetchAccountBar(signal?: AbortSignal): Promise<AccountBar | null> {
  const response = await fetch(`${API_BASE_URL}${ACCOUNT_BAR_PATH}`, {
    credentials: "include",
    headers: { Accept: "application/json", "Accept-Language": i18n.language },
    signal,
  })
  if (response.status === 401) return null
  // Thrown, not answered with null: a refusal (429) or an outage is not "signed out", and
  // the bar must not offer Sign in to someone who is signed in.
  if (!response.ok) throw new Error(`account-bar ${response.status}`)
  return (await response.json()) as AccountBar
}

/** The platform's public branding: its appearance, fonts and hidden languages. */
export async function fetchPlatformBranding(signal?: AbortSignal): Promise<PlatformBranding> {
  const response = await fetch(`${API_BASE_URL}/api/v1/Platform/branding`, {
    credentials: "omit",
    headers: { Accept: "application/json" },
    signal,
  })
  if (!response.ok) throw new Error(`branding ${response.status}`)
  return (await response.json()) as PlatformBranding
}
