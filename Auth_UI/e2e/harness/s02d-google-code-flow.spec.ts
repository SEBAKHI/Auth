import { createHash } from "node:crypto"
import type { Page } from "@playwright/test"

import { loginResponse } from "../isolated/accounts/mock-anonymous-api"
import { HOSTS, ORIGINS, expect, test, type AppName } from "./fixtures"

/**
 * S02 PR D, in the consuming medium: both apps built for production, served with the headers
 * their web.config makes IIS send, signing in with Google by the authorization-code method.
 * Google's authorization page is a stand-in on https://accounts.google.com (topology.ts) that
 * sends the person back to the callback page with a code and the state it received, as Google
 * does; it runs once without and once with Cross-Origin-Opener-Policy: same-origin, which cuts
 * the popup's link to the sign-in page (window.opener is null on return).
 *
 * Proved for each app and each case: the button shows with the page, before the providers'
 * answer; a click signs in through the exchange and external-login, with the server's nonce and
 * the verifier whose S256 challenge went to Google; the popup closes; the main page sends no
 * request to accounts.google.com; zero CSP violations anywhere (the csp fixture, under the
 * production policies); and /google-callback.html is served no-store and no-referrer.
 *
 * Deliberate breaks, each verified red: Google's script tag put back into the main document
 * (the last test, as a standing negative); the channel listener removed from the sign-in page
 * (the cut-link case fails, the opener case passes); the callback page's close() removed.
 */

const NONCE = "harness-server-nonce"
const CODE = "harness-one-time-code"
const ID_TOKEN = "harness.id.token"
const CLIENT_ID = "test-client.apps.googleusercontent.com"
const FIRST_LOAD = { timeout: 20_000 }

const USER = {
  id: "33333333-3333-3333-3333-333333333333",
  email: "gina.google@example.com",
  firstName: "Gina",
  lastName: "Google",
  displayName: "Gina Google",
  status: "Active",
  emailConfirmed: true,
  hasPassword: false,
  twoFactorEnabled: false,
}

/** Google's page: back to redirect_uri with a code and the state it was given, as Google does. */
const GOOGLE_PAGE = `<!doctype html><title>Google</title><script>
  const query = new URLSearchParams(location.search)
  const back = new URL(query.get("redirect_uri"))
  back.searchParams.set("code", ${JSON.stringify(CODE)})
  back.searchParams.set("state", query.get("state") || "")
  back.searchParams.set("scope", "openid email profile")
  location.replace(back.toString())
</script>`

function base64Url(bytes: Buffer) {
  return bytes.toString("base64url")
}

interface Seen {
  exchanges: Array<{ code: string; codeVerifier: string; origin?: string }>
  signIns: Array<{ provider: string; idToken: string; nonce?: string }>
}

/** The anonymous API of a person signing in with Google; the providers' answer waits for `release`. */
async function googleApi(api: import("./fixtures").HarnessApi, held: Promise<void>, seen: Seen) {
  await api.useAnonymous(
    async (route, url, body) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/external-nonce") {
        await route.fulfill({ json: { nonce: NONCE } })
        return true
      }
      if (path === "/api/v1/auth/external/google/code") {
        seen.exchanges.push({
          ...(body as { code: string; codeVerifier: string }),
          origin: route.request().headers()["origin"],
        })
        await route.fulfill({ json: { idToken: ID_TOKEN }, headers: { "cache-control": "no-store" } })
        return true
      }
      if (path === "/api/v1/auth/external-login") {
        seen.signIns.push(body as Seen["signIns"][number])
        await route.fulfill({ json: loginResponse(USER) })
        return true
      }
      return false
    },
    {
      externalProviders: async () => {
        await held
        return [{ code: "google", name: "Google", iconUrl: "", clientId: CLIENT_ID }]
      },
    }
  )
}

/** This browser saw Google offered on its last visit (use-external-providers.ts). */
async function seedProvidersHint(page: Page) {
  await page.addInitScript(() => {
    try {
      localStorage.setItem("auth.ui.externalProviders", JSON.stringify(["google"]))
    } catch {
      // The attacker and Google stand-in pages have storage too; nothing to do there.
    }
  })
}

for (const app of ["accounts", "console"] as const satisfies readonly AppName[]) {
  for (const coop of [false, true]) {
    const label = coop ? "with Google's page cutting the opener (COOP same-origin)" : "with the opener kept"
    test(`s02d: ${app} signs in with Google by the code flow, ${label}`, async ({ page, api, attacker, requests }) => {
      let release!: () => void
      const held = new Promise<void>((resolve) => (release = resolve))
      const seen: Seen = { exchanges: [], signIns: [] }
      await googleApi(api, held, seen)
      attacker.serve(HOSTS.google, "/o/oauth2/v2/auth", GOOGLE_PAGE, coop ? { "cross-origin-opener-policy": "same-origin" } : {})
      await seedProvidersHint(page)

      const mainPageHosts: string[] = []
      page.on("request", (request) => mainPageHosts.push(new URL(request.url()).host))

      await page.goto(`${ORIGINS[app]}/login`)
      const button = page.getByRole("button", { name: "Continue with Google" })
      // With the page, from the hint: the providers' answer is still held.
      await expect(button).toBeVisible(FIRST_LOAD)
      await expect(page.getByText("or", { exact: true })).toBeVisible()
      release()

      const popupOpened = page.waitForEvent("popup")
      await button.click()
      const popup = await popupOpened

      await expect.poll(() => seen.signIns.length, FIRST_LOAD).toBe(1)
      expect(seen.signIns[0]).toMatchObject({ provider: "google", idToken: ID_TOKEN, nonce: NONCE })
      expect(seen.exchanges).toHaveLength(1)
      expect(seen.exchanges[0].code).toBe(CODE)
      expect(seen.exchanges[0].origin).toBe(ORIGINS[app])
      await expect.poll(() => popup.isClosed(), FIRST_LOAD).toBe(true)

      // What Google was asked: the authorization-code request, PKCE S256, the server's nonce.
      const authorize = requests.to(HOSTS.google).filter((entry) => entry.path === "/o/oauth2/v2/auth")
      expect(authorize).toHaveLength(1)
      const query = new URL(authorize[0].url, ORIGINS.google).searchParams
      expect(Object.fromEntries(query)).toEqual({
        client_id: CLIENT_ID,
        redirect_uri: `${ORIGINS[app]}/google-callback.html`,
        response_type: "code",
        scope: "openid email profile",
        state: expect.stringMatching(/^[A-Za-z0-9_-]{43}$/),
        nonce: NONCE,
        code_challenge: base64Url(createHash("sha256").update(seen.exchanges[0].codeVerifier, "ascii").digest()),
        code_challenge_method: "S256",
        prompt: "select_account",
        hl: "en",
      })

      // The callback page took the code out of its address before anything else loaded: its
      // module's request carries no Referer at all (no-referrer).
      const callbackLoads = requests.to(HOSTS[app]).filter((entry) => entry.path === "/google-callback.html")
      expect(callbackLoads).toHaveLength(1)
      const afterCallback = requests.to(HOSTS[app]).filter((entry) => entry.path.startsWith("/assets/google-callback"))
      expect(afterCallback.length).toBeGreaterThan(0)
      for (const entry of afterCallback) expect(entry.headers["referer"]).toBeUndefined()

      // Nothing of Google's in the sign-in page.
      expect(mainPageHosts.length).toBeGreaterThan(0)
      expect(mainPageHosts).not.toContain(HOSTS.google)
      if (app === "accounts") {
        await expect.poll(() => new URL(page.url()).pathname, FIRST_LOAD).not.toBe("/login")
      }
    })
  }
}

test("s02d: /google-callback.html is served no-store and no-referrer, under each app's site policy", async ({
  request,
  harness,
}) => {
  for (const app of ["accounts", "console"] as const) {
    const response = await request.get(`${ORIGINS[app]}/google-callback.html?code=c&state=s`)
    expect(response.status()).toBe(200)
    const headers = response.headers()
    expect(headers["cache-control"]).toBe("no-store")
    expect(headers["referrer-policy"]).toBe("no-referrer")
    expect(headers["x-frame-options"]).toBe("DENY")
    const sitePolicy = harness.models[app].headers.find(([name]) => name === "Content-Security-Policy")?.[1]
    expect(sitePolicy).toBeTruthy()
    expect(headers["content-security-policy"]).toBe(sitePolicy)
    expect(sitePolicy).not.toContain("google")
    // The page itself: one module of this origin, no inline script, nothing else.
    const html = await response.text()
    expect(html).toMatch(/<script type="module" crossorigin src="\/assets\/google-callback-[^"]+\.js"><\/script>/)
    expect(html).not.toMatch(/<script>(?!<\/)/)
    expect(html).not.toContain("theme-boot")
    expect(html).not.toContain("preconnect")
    expect(html).not.toContain('rel="icon"')
  }
})

test("s02d (standing negative): Google's script in the sign-in page is refused by script-src", async ({
  page,
  api,
  csp,
}) => {
  await api.useAnonymous()
  await page.goto(`${ORIGINS.accounts}/login`)
  // Refused by the enforced policy, and reported by its Report-Only twin (card S02 stage A).
  for (const disposition of ["enforce", "report"] as const) {
    csp.expect({
      directive: "script-src",
      blocked: "https://accounts.google.com/gsi/client",
      disposition,
      reason: "S02 PR D: Google's script is no longer a script source of either app",
    })
  }

  await page.evaluate(() => {
    const script = document.createElement("script")
    script.src = "https://accounts.google.com/gsi/client"
    document.head.appendChild(script)
  })

  await expect
    .poll(() => csp.violations().filter((violation) => violation.blocked === "https://accounts.google.com/gsi/client").length)
    .toBeGreaterThanOrEqual(2)
})
