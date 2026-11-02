import { ACCOUNTS_URL, API_BASE_URL } from "@authsystem/api/env"

/*
 * What a linked application adds to show IAM's account bar (owner decisions of 2026-11-02),
 * built from the choices on Platform settings › Account bar. The bar is the platform's, not
 * one application's: the loader's address and the bar's look are the same for every
 * application. Only the sign-out leg is an application's own, which is why the generator
 * asks for one.
 */

/** What the bar shows: both, or one of the two (the element's `hide`). */
export type AccountBarShows = "both" | "apps" | "avatar"

/** The application's colour mode, or the person's own choice in IAM (no `theme`). */
export type AccountBarTheme = "person" | "light" | "dark"

export interface AccountBarChoices {
  shows: AccountBarShows
  theme: AccountBarTheme
  /** The application's own sign-out address, which ends its session and then IAM's. */
  signOutHref: string
  /** Where Sign in takes the page; empty for the accounts sign-in page. */
  signInHref: string
}

export const DEFAULT_ACCOUNT_BAR_CHOICES: AccountBarChoices = {
  shows: "both",
  theme: "person",
  signOutHref: "/sign-out",
  signInHref: "",
}

/** A value inside a double-quoted HTML attribute, as the browser will read it back. */
function attribute(value: string): string {
  return value.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;")
}

/** The two lines for the application's header. */
export function accountBarSnippet(choices: AccountBarChoices, accountsUrl: string = ACCOUNTS_URL): string {
  const attributes: string[] = []
  const signOut = choices.signOutHref.trim()
  if (signOut) attributes.push(`signout-href="${attribute(signOut)}"`)
  const signIn = choices.signInHref.trim()
  if (signIn) attributes.push(`signin-href="${attribute(signIn)}"`)
  // "both" hides nothing; showing only the grid hides the avatar, and the reverse.
  if (choices.shows === "apps") attributes.push(`hide="avatar"`)
  if (choices.shows === "avatar") attributes.push(`hide="apps"`)
  if (choices.theme !== "person") attributes.push(`theme="${choices.theme}"`)
  return [
    `<script async src="${accountsUrl}/embed/v1/account-bar.js"></script>`,
    `<iam-account-bar${attributes.map((item) => ` ${item}`).join("")}></iam-account-bar>`,
  ].join("\n")
}

/**
 * Where the application's own sign-out sends the browser once its session is over: IAM's
 * end-session endpoint, for this application and one of its registered return addresses.
 * IAM asks the person to confirm, ends the single sign-on session, and sends them back there.
 */
export function endSessionUrl(clientId: string, postLogoutRedirectUri: string, apiBaseUrl: string = API_BASE_URL): string {
  const query = new URLSearchParams({ client_id: clientId, post_logout_redirect_uri: postLogoutRedirectUri })
  return `${apiBaseUrl}/api/v1/auth/end-session?${query.toString()}`
}

/** What the application's own Content-Security-Policy must allow for the loader and its frames. */
export function accountBarCsp(accountsUrl: string = ACCOUNTS_URL): string {
  return [`script-src ${accountsUrl}`, `frame-src ${accountsUrl}`].join("\n")
}

/** The sign-in choice that adds no address: Sign in opens IAM's own sign-in page. */
export const IAM_SIGN_IN = "iam"

/**
 * The application's sign-in addresses this IAM knows, in order: its registered initiate-login
 * address, then its base address, then IAM's own sign-in page. Duplicates count once.
 */
export function signInChoices(application: {
  initiateLoginUri?: string | null
  baseUrl?: string | null
}): string[] {
  const addresses = [application.initiateLoginUri, application.baseUrl]
    .map((address) => address?.trim())
    .filter((address): address is string => Boolean(address))
  return [...new Set(addresses), IAM_SIGN_IN]
}
