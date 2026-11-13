import type { Page } from "@playwright/test"

import type { SeenRequest } from "../isolated/accounts/mock-anonymous-api"
import { HOSTS, ORIGINS, expect, test, type AppName } from "./fixtures"

/**
 * S13 in the consuming medium: both apps built for production and served with the headers
 * their web.config makes IIS send, the bot check on, and Cloudflare Turnstile stood in for on
 * https://challenges.cloudflare.com (topology.ts). The stand-in is a test-served api.js that
 * renders the widget as a frame from that host and hands out a token per render or reset:
 * "harness-token-<n>-<action>", so a request shows which widget made its token and for what.
 *
 * Proved on the seven pages that send a protected request (D5): accounts /login, /register,
 * /register/verify (a new code), /forgot-password and /accept-invitation (its code start);
 * console /login and /forgot-password. On each: Cloudflare's script loads once, from that
 * host; the request carries the token made for its own action; and the csp fixture finds zero
 * violations of the production policies (script-src and frame-src admit the host). And the
 * reset: after a refused sign-in the widget makes a fresh token, and the second request
 * carries it. And the box: when Cloudflare asks for a click, the check is shown and the
 * click goes through. And a check that never finishes: after the wait the request goes out
 * without a token (the server counts it), and the check names the ways out.
 *
 * Deliberate breaks, each verified red: challenges.cloudflare.com taken out of either app's
 * frame-src (csp fixture: a frame-src violation); the token left out of the forgot-password
 * body; the widget's reset after a spend removed (the second sign-in waits and is held).
 */

const SITE_KEY = "1x00000000000000000000AA"
const FIRST_LOAD = { timeout: 20_000 }
const API_JS = "/turnstile/v0/api.js"
const FRAME = "/turnstile/frame.html"
const TOKEN = /^harness-token-\d+-(login|register|forgot-password|email-code)$/
/** The class itself, not a variant that names it (the Field carries "[&>.sr-only]:w-auto"). */
const SR_ONLY = /(?:^|\s)sr-only(?:\s|$)/

/**
 * The stand-in's api.js. `interactive`: each widget first asks for a click in its frame, as
 * Cloudflare does for a visitor it is unsure of, and makes its token only after it. `silent`:
 * the widget renders its frame and never answers, as on a network that stalls Cloudflare's
 * challenge.
 */
function standIn(interactive: boolean, silent = false) {
  return `(() => {
  let issued = 0
  const widgets = new Map()
  const say = (text) => console.log("harness-turnstile:" + text)
  function issue(id) {
    const widget = widgets.get(id)
    if (!widget) return
    issued += 1
    const token = "harness-token-" + issued + "-" + widget.options.action
    setTimeout(() => { if (widgets.has(id)) widget.options.callback(token) }, 50)
  }
  window.addEventListener("message", (event) => {
    if (event.origin !== ${JSON.stringify(ORIGINS.cloudflare)} || event.data !== "harness-turnstile-clicked") return
    for (const [id, widget] of widgets) {
      if (!widget.waiting) continue
      widget.waiting = false
      widget.options["after-interactive-callback"]()
      issue(id)
    }
  })
  window.turnstile = {
    render(container, options) {
      const id = "harness-widget-" + Math.random().toString(36).slice(2)
      const frame = document.createElement("iframe")
      frame.src = ${JSON.stringify(ORIGINS.cloudflare + FRAME)}
      frame.title = "Cloudflare security challenge"
      container.appendChild(frame)
      widgets.set(id, { options, frame, waiting: ${interactive} })
      say("render " + options.action + " " + options.appearance + " " + options.language)
      if (${silent}) return id
      if (${interactive}) options["before-interactive-callback"]()
      else issue(id)
      return id
    },
    reset(id) {
      say("reset")
      if (!${silent}) issue(id)
    },
    remove(id) {
      const widget = widgets.get(id)
      if (widget) widget.frame.remove()
      widgets.delete(id)
    },
  }
})()`
}

const FRAME_PAGE = `<!doctype html><title>Cloudflare</title>
<button type="button" onclick="parent.postMessage('harness-turnstile-clicked', '*')">Verify you are human</button>`

async function serveTurnstile(attacker: import("./attacker-origins").AttackerPages, interactive = false, silent = false) {
  attacker.serve(HOSTS.cloudflare, API_JS, standIn(interactive, silent), { "content-type": "text/javascript; charset=utf-8" })
  attacker.serve(HOSTS.cloudflare, FRAME, FRAME_PAGE)
}

/** What the stand-in was asked to do on this page, in order. */
function turnstileLog(page: Page) {
  const said: string[] = []
  page.on("console", (message) => {
    const text = message.text()
    if (text.startsWith("harness-turnstile:")) said.push(text.slice("harness-turnstile:".length))
  })
  return said
}

const tokenOf = (request: SeenRequest | undefined) => (request?.body as { botToken?: string } | undefined)?.botToken
const sent = (seen: SeenRequest[], path: string) => seen.filter((request) => request.path === path)

const problem = (code: string) => ({
  status: 400,
  contentType: "application/problem+json",
  body: JSON.stringify({ type: "about:blank", title: "Bad Request", status: 400, code, detail: code }),
})

const CODE_STARTED = (email: string) => ({
  pendingId: "harness-pending-1",
  maskedEmail: email.replace(/^(.).*@/, "$1***@"),
  // Already past: the code screen's "Send a new code" is ready at once.
  expiresAt: new Date(Date.now() - 1000).toISOString(),
})

for (const app of ["accounts", "console"] as const satisfies readonly AppName[]) {
  test(`s13: ${app} /login sends the sign-in with its token, and a fresh one after a refusal`, async ({
    page,
    api,
    attacker,
    requests,
    csp,
  }) => {
    const seen: SeenRequest[] = []
    await serveTurnstile(attacker)
    await api.useAnonymous(
      async (route, url) => {
        if (url.pathname.toLowerCase() !== "/api/v1/auth/login") return false
        await route.fulfill(problem("User.InvalidCredentials"))
        return true
      },
      { seen, botDefense: { mode: "Enforce", siteKey: SITE_KEY } }
    )
    const said = turnstileLog(page)

    await page.goto(`${ORIGINS[app]}/login`)
    await page.getByLabel("Email").fill("someone@example.com")
    await page.getByLabel("Password", { exact: true }).fill("not-the-password")
    await expect.poll(() => said.filter((line) => line.startsWith("render")), FIRST_LOAD).toEqual([
      "render login interaction-only en",
    ])
    await page.getByRole("button", { name: "Sign in", exact: true }).click()
    await expect.poll(() => sent(seen, "/api/v1/auth/login").length, FIRST_LOAD).toBe(1)

    // Refused: the token went out, the widget makes a fresh one, and the second try carries it.
    await expect.poll(() => said.includes("reset")).toBe(true)
    await page.getByRole("button", { name: "Sign in", exact: true }).click()
    await expect.poll(() => sent(seen, "/api/v1/auth/login").length, FIRST_LOAD).toBe(2)

    const [first, second] = sent(seen, "/api/v1/auth/login").map(tokenOf)
    expect(first).toMatch(TOKEN)
    expect(first).toMatch(/-login$/)
    expect(second).toMatch(/-login$/)
    expect(second).not.toBe(first)
    // One script for the page, from Cloudflare's host only; and the check stayed out of sight.
    expect(requests.to(HOSTS.cloudflare).filter((entry) => entry.path === API_JS)).toHaveLength(1)
    await expect(page.getByTestId("bot-check")).toHaveClass(SR_ONLY)
    expect(csp.violations()).toEqual([])
  })

  test(`s13: ${app} /forgot-password sends the reset request with its token`, async ({ page, api, attacker, csp }) => {
    const seen: SeenRequest[] = []
    await serveTurnstile(attacker)
    await api.useAnonymous(
      async (route, url) => {
        if (url.pathname.toLowerCase() !== "/api/v1/auth/forgot-password") return false
        await route.fulfill({ json: { maskedEmail: "s***@example.com", expiresAt: new Date(Date.now() + 3_600_000).toISOString() } })
        return true
      },
      { seen, botDefense: { mode: "Enforce", siteKey: SITE_KEY } }
    )

    await page.goto(`${ORIGINS[app]}/forgot-password`)
    await page.getByLabel("Email").fill("someone@example.com")
    await page.getByRole("button", { name: "Send reset link" }).click()

    await expect(page.getByText("Check your email")).toBeVisible(FIRST_LOAD)
    const [request] = sent(seen, "/api/v1/auth/forgot-password")
    expect(request.body).toEqual({ email: "someone@example.com", botToken: expect.stringMatching(/-forgot-password$/) })
    expect(csp.violations()).toEqual([])
  })
}

test("s13: accounts /register and /register/verify send each code start with a token of its own", async ({
  page,
  api,
  attacker,
  csp,
}) => {
  const seen: SeenRequest[] = []
  await serveTurnstile(attacker)
  await api.useAnonymous(
    async (route, url, body) => {
      if (url.pathname.toLowerCase() !== "/api/v1/auth/email-code/start") return false
      await route.fulfill({ json: CODE_STARTED((body as { email: string }).email) })
      return true
    },
    { seen, botDefense: { mode: "Enforce", siteKey: SITE_KEY } }
  )

  await page.goto(`${ORIGINS.accounts}/register`)
  await page.getByLabel("Email", { exact: true }).fill("new.person@example.com")
  await page.getByRole("button", { name: "Send code" }).click()
  await expect(page).toHaveURL(/\/register\/verify/, FIRST_LOAD)

  // The code screen's own check: a new code goes out with a token made for it.
  const again = page.getByRole("button", { name: "Send a new code" })
  await expect(again).toBeEnabled(FIRST_LOAD)
  await again.click()
  await expect.poll(() => sent(seen, "/api/v1/auth/email-code/start").length, FIRST_LOAD).toBe(2)

  const [start, resend] = sent(seen, "/api/v1/auth/email-code/start").map(tokenOf)
  expect(start).toMatch(/-email-code$/)
  expect(resend).toMatch(/-email-code$/)
  expect(resend).not.toBe(start)
  expect(csp.violations()).toEqual([])
})

test("s13: accounts /accept-invitation sends its code start with a token", async ({ page, api, attacker, csp }) => {
  const seen: SeenRequest[] = []
  const token = "harness-invitation-token"
  await serveTurnstile(attacker)
  await api.useAnonymous(
    async (route, url, body) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/invitations/${token}`) {
        await route.fulfill({
          json: {
            id: "44444444-4444-4444-4444-444444444444",
            organizationName: "Northwind",
            email: "rep@example.com",
            roleName: "Member",
            invitedByName: "Ada Admin",
            status: "Pending",
            expiresAt: "2099-12-01T00:00:00Z",
            isExpired: false,
            isAlreadyMember: false,
            userExists: false,
            applicationName: null,
            applicationLogoUrl: null,
            applicationLogoUrlDark: null,
            applicationRoleName: null,
          },
        })
        return true
      }
      if (path === `/api/v1/invitations/${token}/register`) {
        await route.fulfill({
          json: {
            userId: "66666666-6666-6666-6666-666666666666",
            email: "rep@example.com",
            organizationName: "Northwind",
            roleName: "Member",
            organizationId: "55555555-5555-5555-5555-555555555555",
            applicationRoleOutcome: "NotRequested",
            application: null,
            launchUrl: null,
          },
        })
        return true
      }
      if (path === "/api/v1/auth/email-code/start") {
        await route.fulfill({ json: CODE_STARTED((body as { email: string }).email) })
        return true
      }
      return false
    },
    {
      seen,
      botDefense: { mode: "Enforce", siteKey: SITE_KEY },
      // A sign-up by code: no password asked, so the sign-in after it is a code start.
      branding: {
        codeSignInOffered: true,
        passkeySignInOffered: false,
        primaryMethod: "code",
        selfRegistrationOpen: true,
        passwordAtSignUp: false,
        nameAtSignUp: false,
      },
    }
  )

  await page.goto(`${ORIGINS.accounts}/accept-invitation?token=${token}`)
  await page.getByRole("button", { name: "Create account & join" }).click()

  await expect(page.getByRole("heading", { name: "Check your email" })).toBeVisible(FIRST_LOAD)
  const [start] = sent(seen, "/api/v1/auth/email-code/start")
  expect(start.body).toMatchObject({ email: "rep@example.com", botToken: expect.stringMatching(/-email-code$/) })
  expect(csp.violations()).toEqual([])
})

test("s13: a check Cloudflare asks a click for is shown, and the click lets the request go", async ({
  page,
  api,
  attacker,
  csp,
}) => {
  const seen: SeenRequest[] = []
  await serveTurnstile(attacker, true)
  await api.useAnonymous(
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/auth/forgot-password") return false
      await route.fulfill({ json: { maskedEmail: "s***@example.com", expiresAt: null } })
      return true
    },
    { seen, botDefense: { mode: "Enforce", siteKey: SITE_KEY } }
  )

  await page.goto(`${ORIGINS.accounts}/forgot-password`)
  const check = page.getByTestId("bot-check")
  await expect(check).toHaveAttribute("data-state", "interactive", FIRST_LOAD)
  await expect(check).not.toHaveClass(SR_ONLY)
  const frame = page.frameLocator('iframe[title="Cloudflare security challenge"]')
  await expect(frame.getByRole("button", { name: "Verify you are human" })).toBeVisible()

  await page.getByLabel("Email").fill("someone@example.com")
  await page.getByRole("button", { name: "Send reset link" }).click()
  await frame.getByRole("button", { name: "Verify you are human" }).click()

  await expect(page.getByText("Check your email")).toBeVisible(FIRST_LOAD)
  expect(tokenOf(sent(seen, "/api/v1/auth/forgot-password")[0])).toMatch(/-forgot-password$/)
  expect(csp.violations()).toEqual([])
})

test("s13: a check that never finishes: the request goes out without a token after the wait, and the check names the ways out", async ({
  page,
  api,
  attacker,
  csp,
}) => {
  // The page waits ENFORCED_WAIT_MS (15 s) for a token before it gives up.
  test.setTimeout(60_000)
  const seen: SeenRequest[] = []
  await serveTurnstile(attacker, false, true)
  await api.useAnonymous(
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/auth/login") return false
      await route.fulfill(problem("BotDefense.TokenMissing"))
      return true
    },
    { seen, botDefense: { mode: "Enforce", siteKey: SITE_KEY } }
  )
  const said = turnstileLog(page)

  await page.goto(`${ORIGINS.accounts}/login`)
  await page.getByLabel("Email").fill("someone@example.com")
  await page.getByLabel("Password", { exact: true }).fill("not-the-password")
  await expect.poll(() => said.filter((line) => line.startsWith("render")), FIRST_LOAD).toEqual([
    "render login interaction-only en",
  ])
  const clickedAt = Date.now()
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  // Held while the widget may still answer, then sent without a token: the server refuses it
  // as TokenMissing and counts it, which is what the owner's spike watch reads.
  await expect.poll(() => sent(seen, "/api/v1/auth/login").length, { timeout: 30_000 }).toBe(1)
  expect(Date.now() - clickedAt).toBeGreaterThanOrEqual(14_000)
  expect(tokenOf(sent(seen, "/api/v1/auth/login")[0])).toBeUndefined()

  const check = page.getByTestId("bot-check")
  await expect(check).toHaveAttribute("data-state", "stalled")
  await expect(check).not.toHaveClass(SR_ONLY)
  await expect(check.getByText("The security check didn't finish")).toBeVisible()
  await expect(check.getByRole("button", { name: "Retry" })).toBeVisible()
  expect(csp.violations()).toEqual([])
})
