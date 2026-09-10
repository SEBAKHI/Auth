import type { Page, Route } from "@playwright/test"

import { fulfillJson } from "../mock-authenticated-api"

export { fulfillJson }

/** One request the page made, as the test may want to assert on it. */
export interface SeenRequest {
  method: string
  path: string
  body: unknown
}

/** A signed-in body the sign-up completion may answer with. */
export function loginResponse(user: Record<string, unknown>) {
  const encode = (value: object) =>
    Buffer.from(JSON.stringify(value)).toString("base64url")
  const accessToken = `${encode({ alg: "none", typ: "JWT" })}.${encode({
    exp: Math.floor(Date.now() / 1000) + 3600,
    sub: user.id,
  })}.signature`
  return {
    token: { accessToken, refreshToken: "isolated-refresh" },
    user,
    requiresPasswordChange: false,
  }
}

/**
 * The anonymous twin of `installAuthenticatedApi`: no refresh token is seeded,
 * so the app boots signed out, and only what a stranger's screens ask for is
 * answered by default - branding, the password policy, the provider list.
 *
 * Every non-GET request is recorded with its parsed body, in order, so a test
 * can assert what was SENT and not merely what was rendered: which fields the
 * sign-up completion carried, whether the code screen asked for a code on
 * mount, how many times a form was submitted. Anything the handler does not
 * claim is refused with a distinct 404, never passed to the network.
 */
export async function installAnonymousApi(
  page: Page,
  handle: (route: Route, url: URL, body: unknown) => Promise<boolean>,
  options?: { seen?: SeenRequest[] }
) {
  await page.route("**/api/v1/**", async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname.toLowerCase()
    const raw = request.postData()
    const body = raw ? (JSON.parse(raw) as unknown) : undefined
    if (request.method() !== "GET") {
      options?.seen?.push({ method: request.method(), path, body })
    }

    if (path === "/api/v1/platform/branding") {
      await fulfillJson(route, { platformName: "AuthSystem" })
      return
    }
    if (path === "/api/v1/platform/password-policy") {
      await fulfillJson(route, {
        minimumLength: 8,
        requireUppercase: true,
        requireLowercase: true,
        requireDigit: true,
        requireSpecialCharacter: true,
      })
      return
    }
    if (path === "/api/v1/auth/external-providers") {
      await fulfillJson(route, [])
      return
    }
    if (await handle(route, url, body)) return

    await fulfillJson(route, { title: "Unexpected isolated API request" }, 404)
  })
}
