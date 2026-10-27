import { getValidReturnTo } from "@authsystem/auth/return-to"
import type { EntryContext, EntryContinuation } from "@authsystem/i18n/flow-hints"

/**
 * Pages that exist only inside a sign-in already under way. The page before
 * them hands over the pending request in router state, not in the address, so
 * a reload of one arrives with neither a request nor a hint in its URL and is
 * still that flow.
 */
const CONTINUES_ALWAYS = [
  "/two-factor",
  "/two-factor/required",
  "/force-password-change",
  "/verify-email",
  "/register/complete",
  "/account-recovery",
]

/**
 * Pages an endpoint sends the browser to with the application's `client_id`:
 * the expired-request page (from the resume) and the two sign-out pages (from
 * end-session). They continue the kept flow only for the same application.
 */
const CONTINUES_SAME_CLIENT = ["/sign-in-expired", "/logout", "/signed-out"]

function continuationOf(pathname: string): EntryContinuation {
  const path = pathname.length > 1 ? pathname.replace(/\/+$/, "") : pathname
  if (CONTINUES_ALWAYS.includes(path)) return "always"
  if (CONTINUES_SAME_CLIENT.includes(path)) return "same-client"
  return "never"
}

/**
 * What the page the tab arrived at says about the kept hints: the pending
 * request it serves (read by `return-to.ts`, the one module that knows its
 * shapes), the application it names, and how it relates to the kept flow.
 * Every other accounts page — the profile, the organizations, a sign-in page
 * opened with no request, a link from an email — is a fresh visit.
 */
export function entryContextFor(location: Pick<Location, "pathname" | "search">): EntryContext {
  return {
    pendingRequest: getValidReturnTo(location.search),
    clientId: new URLSearchParams(location.search).get("client_id"),
    continuation: continuationOf(location.pathname),
  }
}
