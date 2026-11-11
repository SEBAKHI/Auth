import type { Page, Route } from "@playwright/test"

import { en } from "../../packages/i18n/src/locales/en"
import type { AttackerPages } from "./attacker-origins"
import { HOSTS, ORIGINS, expect, test, type HarnessApi } from "./fixtures"
import { fulfillJson } from "../isolated/mock-authenticated-api"
import {
  loginResponse,
  type SeenRequest,
} from "../isolated/accounts/mock-anonymous-api"
import {
  PASSKEY_RP_ID,
  USER_HANDLE,
  assertionOptions,
  attachVirtualAuthenticator,
  attestationFormat,
  challenge,
  clientData,
} from "./passkey-authenticator"

/**
 * S20 PR C in a real browser (PK18), on the S30a harness: every page runs under
 * its app's real web.config CSP, and the csp fixture fails the test on any
 * violation beyond the register. A CDP virtual authenticator stands in for the
 * device; the API host is the harness model, which answers the passkey routes,
 * records what the browser signed (the origin, `clientDataJSON`'s type) and the
 * attestation format each registration sends (T-C11).
 *
 * What it proves: a passkey added in the console signs in at the accounts app
 * (one RP ID, the registrable domain both share); the ceremonies run under the
 * shipped CSP; the click starts them; a sign-in an application asked for ends
 * back at that application. What it does not: that the server checks
 * the signature, the origin or the counter (the xUnit suite and PasskeyDevRun
 * do), nor a real device (PasskeyRealDeviceCheck, the owner's hands).
 *
 * Deliberate break this file must catch: `isUserVerified: false` (the
 * authenticator cannot verify the person, and the ceremony is refused); and the
 * sign-in page's passkey exit leaving by anything but `complete` (the application's
 * request is dropped, and the person never gets back to it).
 */

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "admin@example.com",
  firstName: "Ada",
  lastName: "Admin",
  displayName: "Ada Admin",
  status: "Active",
  emailConfirmed: true,
  hasPassword: true,
  twoFactorEnabled: true,
  totpEnabled: true,
  qualifyingMfa: true,
  emailCodeAvailable: true,
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

interface Registered {
  credentialId: string
  name: string
  origin: string
  type: string
  fmt: string
}

interface Asserted {
  credentialId: string
  origin: string
  type: string
}

function passkeyDto(passkey: Registered) {
  return {
    id: "aaaaaaaa-0000-0000-0000-000000000001",
    name: passkey.name,
    createdAt: "2026-11-08T09:00:00Z",
    lastUsedAt: null,
    isBackupEligible: false,
    isBackedUp: false,
    lastCounterRegressionAt: null,
    credentialId: passkey.credentialId,
  }
}

const FIRST_LOAD = { timeout: 20_000 }

/** The browser of this page offers no passkey suggestion (conditional mediation). */
async function withoutSuggestion(page: import("@playwright/test").Page) {
  await page.addInitScript(() => {
    Object.defineProperty(PublicKeyCredential, "isConditionalMediationAvailable", {
      configurable: true,
      value: async () => false,
    })
  })
}

/** OI-280: the deployment's hint that passkeys may run on the sign-in page. */
const PASSKEYS_MAY_RUN = { passkeySignInOffered: true }

/**
 * The accounts app's anonymous model: passkey options, the sign-in page's lookup
 * after the address (OI-280: `holders` have a passkey, every other address gets
 * the same false), and a sign-in that records what was signed. `hold` keeps the
 * sign-in's answer back until the test lets it go, so the page is still there to
 * be read; `holdLookup` does the same for the lookup's answer.
 */
function anonymousPasskeyModel(
  asserted: Asserted[],
  hold: Promise<void> = Promise.resolve(),
  {
    holders = [USER.email],
    holdLookup = Promise.resolve(),
  }: { holders?: string[]; holdLookup?: Promise<void> } = {}
) {
  return async (route: import("@playwright/test").Route, url: URL) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/passkeys/assertion/options") {
      await fulfillJson(route, assertionOptions())
      return true
    }
    if (path === "/api/v1/auth/passkeys/lookup") {
      const body = route.request().postDataJSON() as { email: string }
      await holdLookup
      await fulfillJson(route, { passkey: holders.includes(body.email) })
      return true
    }
    if (path === "/api/v1/auth/email-code/start") {
      await fulfillJson(route, {
        pendingId: "harness-pending",
        maskedEmail: "a***@example.com",
        expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
      })
      return true
    }
    if (path === "/api/v1/auth/passkeys/assertion/verify") {
      const body = route.request().postDataJSON() as { credential: { id: string } }
      const signed = clientData(body.credential)
      asserted.push({ credentialId: body.credential.id, origin: signed.origin, type: signed.type })
      await hold
      await fulfillJson(route, loginResponse(USER))
      return true
    }
    return false
  }
}

test("s20: a passkey added in the console signs in at the accounts app", async ({ page, api }) => {
  const authenticator = await attachVirtualAuthenticator(page)
  const registered: Registered[] = []
  const asserted: Asserted[] = []

  // The console, signed in: Profile › Security › Passkeys.
  await api.useAuthenticated(["users:read"], async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (path === "/api/v1/users/me") {
      await fulfillJson(route, { ...USER, passkeyCount: registered.length })
      return true
    }
    if (path === "/api/v1/auth/passkeys" && method === "GET") {
      await fulfillJson(route, { rpId: PASSKEY_RP_ID, userHandle: USER_HANDLE, passkeys: registered.map(passkeyDto) })
      return true
    }
    if (path === "/api/v1/auth/passkeys/registration/options") {
      await fulfillJson(route, {
        options: {
          rp: { id: PASSKEY_RP_ID, name: "AuthSystem" },
          user: { id: USER_HANDLE, name: USER.email, displayName: USER.displayName },
          challenge: challenge(),
          pubKeyCredParams: [
            { type: "public-key", alg: -7 },
            { type: "public-key", alg: -257 },
          ],
          timeout: 300_000,
          attestation: "none",
          excludeCredentials: [],
          authenticatorSelection: { residentKey: "required", requireResidentKey: true, userVerification: "required" },
        },
        expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        emailCodeRequired: false,
      })
      return true
    }
    if (path === "/api/v1/auth/passkeys/registration/verify") {
      const body = route.request().postDataJSON() as { credential: { id: string }; name: string | null }
      const signed = clientData(body.credential)
      const passkey = {
        credentialId: body.credential.id,
        name: body.name ?? "Passkey",
        origin: signed.origin,
        type: signed.type,
        fmt: attestationFormat(body.credential),
      }
      registered.push(passkey)
      await fulfillJson(route, passkeyDto(passkey))
      return true
    }
    if (path === "/api/v1/auth/2fa/status") {
      await fulfillJson(route, { recoveryCodesRemaining: 8 })
      return true
    }
    return false
  })

  await page.goto(`${ORIGINS.console}/profile?tab=security`)
  const card = page.getByTestId("passkeys-card")
  await expect(card.getByText("No passkeys yet")).toBeVisible(FIRST_LOAD)

  await card.getByRole("button", { name: "Add a passkey" }).click()
  const dialog = page.getByRole("dialog", { name: "Add a passkey" })
  await dialog.getByLabel("Name").fill("Harness laptop")
  await dialog.getByRole("button", { name: "Create the passkey" }).click()

  await expect(card.getByTestId("passkey-row")).toHaveCount(1)
  await expect(card.getByText("Harness laptop")).toBeVisible()
  expect(registered).toEqual([
    expect.objectContaining({ name: "Harness laptop", origin: ORIGINS.console, type: "webauthn.create" }),
  ])
  // T-C11: the format each browser sends, recorded (the server admits only none,
  // or packed self-attestation without x5c).
  test.info().annotations.push({ type: "fmt", description: registered[0].fmt })
  expect(["none", "packed"]).toContain(registered[0].fmt)
  expect(await authenticator.credentialCount()).toBe(1)

  // The accounts app, signed out: the passkey signs in, two factors in one step.
  await api.useAnonymous(anonymousPasskeyModel(asserted), {
    branding: PASSKEYS_MAY_RUN,
  })

  // The passkey screen, not the address field's suggestion: on this page the
  // browser says it offers no suggestion (T-C5). The suggestion has its own test
  // below. OI-280: no button for every visitor; the address and "Continue" lead to
  // the passkey screen, which opens the browser's window by itself (PR B, T-B14),
  // and the virtual authenticator answers it: no click.
  await withoutSuggestion(page)
  await page.goto(`${ORIGINS.accounts}/login`)
  await expect(page.getByLabel("Email")).toHaveAttribute(
    "autocomplete",
    "username"
  )
  await page.getByLabel("Email").fill(USER.email)
  await page.getByRole("button", { name: "Continue", exact: true }).click(FIRST_LOAD)

  await expect.poll(() => asserted.length, FIRST_LOAD).toBe(1)
  expect(asserted[0]).toEqual({
    credentialId: registered[0].credentialId,
    origin: ORIGINS.accounts,
    type: "webauthn.get",
  })
  // The page left the sign-in form: the session was adopted.
  await expect.poll(() => new URL(page.url()).pathname).not.toBe("/login")
})

test("s20: where the browser offers it, the address field's suggestion signs in with the passkey", async ({
  page,
  api,
}) => {
  // Chromium's conditional mediation, answered by a virtual authenticator that
  // simulates the person's presence: the suggestion's request completes without
  // a click. On a real device the person picks the passkey from the address
  // field's list; that leg is the owner's (PasskeyRealDeviceCheck).
  const authenticator = await attachVirtualAuthenticator(page)
  const credentialId = await authenticator.addCredential()
  const asserted: Asserted[] = []
  // The suggestion completes as soon as it starts here: the sign-in's answer is
  // held until the page has been read, or it could leave before the read.
  let release!: () => void
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  await api.useAnonymous(anonymousPasskeyModel(asserted, held))

  await page.goto(`${ORIGINS.accounts}/login`)

  await expect.poll(() => asserted.length, FIRST_LOAD).toBe(1)
  expect(asserted[0]).toEqual({ credentialId, origin: ORIGINS.accounts, type: "webauthn.get" })
  await expect(page.getByLabel("Email")).toHaveAttribute("autocomplete", "username webauthn")

  release()
  await expect.poll(() => new URL(page.url()).pathname).not.toBe("/login")
})

/**
 * An application sent the person to the accounts sign-in page, with its stored
 * request in the URL. Every way of signing in with the passkey must end at that
 * application: the page resumes the request with one top-level navigation, and
 * the server's answer to the resume is a redirect to the application with its
 * code. That answer is the model's here: that the real server takes this session
 * for a signed-in browser is not proven here. FirstPartySignInExitCoverageTests
 * holds that the verify endpoint delivers its session through the filter that
 * writes the IdP cookie, and PasskeyDevRun's `app-return` scenario runs the whole
 * way against the real server.
 */
const HANDLE = "0123456789abcdef".repeat(4)
const PENDING = `client_id=CRM&auth_request=${HANDLE}`

// OI-280 (V11): the screen's way is the address, "Continue" and the passkey screen,
// which opens the browser's window by itself (PR B, T-B14, V-B16): the virtual
// authenticator answers it and nothing is clicked. The suggestion and the leading
// button under mfa_passkey are as they were. Deliberate breaks, each verified red:
// the page goes to the passkey screen without asking (every address taken for one
// that has a passkey); the screen's auto-start removed (the leg waits on a click).
const WAYS_BACK = [
  {
    name: "the passkey screen, which opens the window by itself, after the address",
    query: PENDING,
    way: "screen" as const,
  },
  {
    name: "the address field's suggestion",
    query: PENDING,
    way: "suggestion" as const,
  },
  {
    name: "the button, when the application asks for the passkey (mfa_passkey)",
    query: `${PENDING}&reason=mfa_passkey&method=password`,
    way: "leading" as const,
  },
]

for (const way of WAYS_BACK) {
  test(`s20: signing in with ${way.name} ends back at the application that asked`, async ({
    page,
    api,
    attacker,
  }) => {
    const authenticator = await attachVirtualAuthenticator(page)
    const credentialId = await authenticator.addCredential()
    const asserted: Asserted[] = []
    const resumes: string[] = []
    const seen: SeenRequest[] = []
    const passkeyModel = anonymousPasskeyModel(asserted)
    const callback = attacker.serve(HOSTS.linkedApp, "/callback", "<!doctype html><title>The application</title>")
    const landing = `${callback}?code=harness-code&state=harness-state`

    // OI-280 PR B (V-B12): on this path only the offer reads the account, and a
    // passkey sign-in opens none. The host answers the read 404 if it comes.
    const accountReads: string[] = []
    await api.useAnonymous(
      async (route, url) => {
        if (url.pathname.toLowerCase() === "/api/v1/users/me") {
          accountReads.push(url.pathname)
          return false
        }
        if (
          url.pathname.toLowerCase() === "/api/v1/auth/authorize/resume" &&
          route.request().method() === "GET"
        ) {
          resumes.push(url.search)
          await route.fulfill({ status: 302, headers: { location: landing } })
          return true
        }
        return passkeyModel(route, url)
      },
      { seen, branding: way.way === "screen" ? PASSKEYS_MAY_RUN : undefined }
    )

    if (way.way !== "suggestion") await withoutSuggestion(page)
    await page.goto(`${ORIGINS.accounts}/login?${way.query}`)

    if (way.way === "screen") {
      await page.getByLabel("Email").fill(USER.email)
      await page
        .getByRole("button", { name: "Continue", exact: true })
        .click(FIRST_LOAD)
      // No click: the screen opens the window, and the authenticator answers it.
    }
    if (way.way === "leading") {
      const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with a passkey" })
      await expect(passkey).toBeEnabled(FIRST_LOAD)
      await passkey.click()
    }

    await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
    expect(asserted).toEqual([{ credentialId, origin: ORIGINS.accounts, type: "webauthn.get" }])
    // One navigation, carrying the request and nothing of the sign-in's words.
    expect(resumes).toEqual([`?${PENDING}`])
    // No offer after a passkey sign-in (T-B9).
    expect(accountReads).toEqual([])
    // The address was asked about once, on the way to the screen, and nothing was emailed.
    expect(seen.map((request) => request.path)).toEqual([
      ...(way.way === "screen" ? ["/api/v1/auth/passkeys/lookup"] : []),
      "/api/v1/auth/passkeys/assertion/verify",
    ])
  })
}

/**
 * OI-280 (T-A8, D-S20-16): "Try another way" opens the other ways on the passkey
 * screen itself; the code is today's, sent for the address on the screen.
 */
test("s20: try another way on the passkey screen: the code, sent for the address on the screen", async ({
  page,
  api,
}) => {
  const seen: SeenRequest[] = []
  await api.useAnonymous(anonymousPasskeyModel([]), {
    seen,
    branding: {
      ...PASSKEYS_MAY_RUN,
      codeSignInOffered: true,
      primaryMethod: "password",
    },
  })

  await withoutSuggestion(page)
  await page.goto(`${ORIGINS.accounts}/login`)
  await page.getByLabel("Email").fill(USER.email)
  await page
    .getByRole("button", { name: "Continue", exact: true })
    .click(FIRST_LOAD)
  await expect(
    page.getByTestId("passkey-sign-in").getByRole("button")
  ).toBeEnabled(FIRST_LOAD)
  // Nothing has been sent but the question.
  expect(seen.map((request) => request.path)).toEqual([
    "/api/v1/auth/passkeys/lookup",
  ])

  await page.getByRole("button", { name: "Try another way" }).click()
  const list = page.getByTestId("other-ways")
  await expect(list.getByRole("button")).toHaveText([
    "Sign in with a code sent to your email",
    "Sign in with a password instead",
  ])
  await list
    .getByRole("button", { name: "Sign in with a code sent to your email" })
    .click()

  await expect(
    page.getByRole("heading", { name: "Check your email" })
  ).toBeVisible(FIRST_LOAD)
  expect(seen).toEqual([
    {
      method: "POST",
      path: "/api/v1/auth/passkeys/lookup",
      body: { email: USER.email },
    },
    {
      method: "POST",
      path: "/api/v1/auth/email-code/start",
      body: expect.objectContaining({ email: USER.email }),
    },
  ])
})

/**
 * OI-280 (D-S20-10, D-S20-15): an address the server does not know answers as an
 * address without a passkey, and the page goes on as before it asked: the code is
 * sent, and there is no passkey screen. The code waits for the answer.
 * Deliberate break, verified red: the code start sent before the lookup answers.
 */
test("s20: an unknown address gets the code, after the answer, and no passkey screen", async ({
  page,
  api,
}) => {
  const seen: SeenRequest[] = []
  let answer!: () => void
  const lookupHeld = new Promise<void>((resolve) => {
    answer = resolve
  })
  await api.useAnonymous(
    anonymousPasskeyModel([], Promise.resolve(), { holdLookup: lookupHeld }),
    {
      seen,
      branding: {
        ...PASSKEYS_MAY_RUN,
        codeSignInOffered: true,
        primaryMethod: "code",
      },
    }
  )

  await withoutSuggestion(page)
  await page.goto(`${ORIGINS.accounts}/login`)
  await page.getByLabel("Email").fill("nobody@example.com")
  await page
    .getByRole("button", { name: "Continue with email" })
    .click(FIRST_LOAD)

  await expect
    .poll(() => seen.map((request) => request.path))
    .toEqual(["/api/v1/auth/passkeys/lookup"])
  // While the answer is on its way, no code has been sent.
  await page.waitForTimeout(300)
  expect(seen.map((request) => request.path)).toEqual([
    "/api/v1/auth/passkeys/lookup",
  ])
  answer()

  await expect(
    page.getByRole("heading", { name: "Check your email" })
  ).toBeVisible(FIRST_LOAD)
  await expect(
    page.getByRole("heading", { name: "Use your passkey" })
  ).toHaveCount(0)
  expect(seen.map((request) => request.path)).toEqual([
    "/api/v1/auth/passkeys/lookup",
    "/api/v1/auth/email-code/start",
  ])
})

/**
 * OI-280 (T-A9, D-152-2): under mfa_passkey every other way would resume the
 * request into the application's refusal, so the list holds the way out alone. It
 * drops the request; the list then offers the other ways, and a password sign-in
 * lands on Security, with no resume.
 */
test("s20: under mfa_passkey the list holds the lost-passkey link alone, and it drops the request", async ({
  page,
  api,
}) => {
  const resumes: string[] = []
  const passkeyModel = anonymousPasskeyModel([])
  await api.useAnonymous(
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/authorize/resume") {
        resumes.push(url.search)
        await route.fulfill({
          status: 302,
          headers: { location: `${ORIGINS.accounts}/never` },
        })
        return true
      }
      if (path === "/api/v1/auth/login") {
        await fulfillJson(route, loginResponse(USER))
        return true
      }
      return passkeyModel(route, url)
    },
    { branding: PASSKEYS_MAY_RUN }
  )

  await withoutSuggestion(page)
  await page.goto(
    `${ORIGINS.accounts}/login?${PENDING}&reason=mfa_passkey&method=password`
  )
  await page.getByLabel("Email").fill(USER.email)
  await page
    .getByRole("button", { name: "Continue", exact: true })
    .click(FIRST_LOAD)
  await expect(
    page.getByRole("heading", { name: "Use your passkey" })
  ).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: "Try another way" }).click()

  const list = page.getByTestId("other-ways")
  await expect(list.getByRole("link")).toHaveText(["I lost my passkey"])
  await expect(list.getByRole("button")).toHaveCount(0)
  await list.getByTestId("passkey-lost-link").click()
  await expect.poll(() => new URL(page.url()).search).toBe("")

  // The request is dropped: the same list now offers the other ways.
  await list
    .getByRole("button", { name: "Sign in with a password instead" })
    .click()
  await page.getByLabel("Password").fill("the password")
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  await expect
    .poll(() => {
      const { pathname, search } = new URL(page.url())
      return `${pathname}${search}`
    })
    .toBe("/profile?tab=security")
  expect(resumes).toEqual([])
})

/**
 * D-152-2: an application requires two steps, and the account's only factor is a
 * lost passkey. A sign-in from this page would resume the request, and the server
 * would end it at the application with `access_denied`, so the person would never
 * see the Security page the sentence names. The link drops the request first.
 * Deliberate break: remove the link.
 */
test("s20: the lost-passkey link under mfa_passkey drops the request: a password sign-in lands on Security, with no resume", async ({
  page,
  api,
}) => {
  const resumes: string[] = []
  await api.useAnonymous(async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/authorize/resume") {
      resumes.push(url.search)
      await route.fulfill({ status: 302, headers: { location: `${ORIGINS.accounts}/never` } })
      return true
    }
    if (path === "/api/v1/auth/passkeys/assertion/options") {
      await fulfillJson(route, assertionOptions())
      return true
    }
    if (path === "/api/v1/auth/login") {
      await fulfillJson(route, loginResponse(USER))
      return true
    }
    return false
  })

  await withoutSuggestion(page)
  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_passkey&method=password`)
  await page.getByTestId("passkey-lost-link").click({ timeout: FIRST_LOAD.timeout })
  await expect.poll(() => new URL(page.url()).search).toBe("")

  await page.getByLabel("Email").fill(USER.email)
  await page.getByLabel("Password").fill("the password")
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  await expect.poll(() => {
    const { pathname, search } = new URL(page.url())
    return `${pathname}${search}`
  }).toBe("/profile?tab=security")
  expect(resumes).toEqual([])
})

const ANOTHER_ACCOUNT = {
  ...USER,
  id: "22222222-2222-2222-2222-222222222222",
  email: "another@example.com",
  firstName: "Bea",
  lastName: "Other",
  displayName: "Bea Other",
}

/**
 * A passkey of another account signs that account in, in place. The tab that signs
 * in resets itself; another tab of the origin still holds the old account's page
 * and rows, while the session it shares is now the new account's. A refresh there
 * would then read and write as the new account under the old one's page. It is
 * told, and starts again as the account the session holds. Deliberate break: remove
 * the comparison that tells it (`publishAccountChanged` in the adoption).
 */
test("s20: a passkey of another account in one tab reaches the old account's other tabs", async ({
  page,
  context,
  api,
}) => {
  const authenticator = await attachVirtualAuthenticator(page)
  await authenticator.addCredential()
  const state = { requirement: "passkey", me: USER as typeof USER }
  await api.useAuthenticated(
    ["users:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/users/me") {
        await fulfillJson(route, state.me)
        return true
      }
      if (path === "/api/v1/auth/2fa/status") {
        await fulfillJson(route, { recoveryCodesRemaining: 8 })
        return true
      }
      if (path === "/api/v1/auth/passkeys/assertion/options") {
        await fulfillJson(route, assertionOptions())
        return true
      }
      if (path === "/api/v1/auth/passkeys/assertion/verify") {
        state.requirement = "none"
        state.me = ANOTHER_ACCOUNT
        await fulfillJson(route, loginResponse(ANOTHER_ACCOUNT))
        return true
      }
      return false
    },
    { mfaRequirement: () => state.requirement }
  )

  // The other tab, on the one page that stays open while a factor is owed.
  const other = await context.newPage()
  await other.goto(`${ORIGINS.console}/profile`)
  await expect(other.getByText(USER.email)).toBeVisible(FIRST_LOAD)

  await page.goto(`${ORIGINS.console}/two-factor/required`)
  const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with your passkey" })
  await expect(passkey).toBeEnabled(FIRST_LOAD)
  await passkey.click()

  await expect(other.getByText(ANOTHER_ACCOUNT.email)).toBeVisible(FIRST_LOAD)
  await expect(other.getByText(USER.email)).toHaveCount(0)
})

test("s20: a page on the apex obtains an assertion, and nothing more", async ({ page, attacker }) => {
  // H8: the RP ID alone does not protect. A page under the registrable domain
  // may ask the browser for the passkey, and the browser signs its origin; the
  // server refuses that origin (it is not a first-party one), which the xUnit
  // suite and PasskeyDevRun prove. Hence the deployment guide's rule: put the
  // applications under a dedicated parent host.
  const authenticator = await attachVirtualAuthenticator(page)
  const credentialId = await authenticator.addCredential()

  await page.goto(attacker.serve(HOSTS.apex, "/passkey", "<!doctype html><title>apex</title>"))
  const signed = await page.evaluate(async (rpId) => {
    const credential = (await navigator.credentials.get({
      publicKey: {
        challenge: crypto.getRandomValues(new Uint8Array(32)),
        rpId,
        allowCredentials: [],
        userVerification: "required",
      },
    })) as PublicKeyCredential | null
    if (!credential) return null
    const response = credential.response as AuthenticatorAssertionResponse
    return {
      id: credential.id,
      clientData: JSON.parse(new TextDecoder().decode(response.clientDataJSON)) as { origin: string },
    }
  }, PASSKEY_RP_ID)

  expect(signed?.id).toBe(credentialId)
  expect(signed?.clientData.origin).toBe(ORIGINS.apex)
})

// ── OI-280 PR B: the offer after a sign-in, and the set-up page's passkey ─────

/**
 * OI-280 PR B (T-B9, T-B8, V-B12): after a sign-in that did not use a passkey,
 * while passkeys may run and this device can make one, the offer to add one stands
 * between the sign-in and where it goes; and the set-up page of an application's
 * two-step requirement offers a passkey beside the app. The API host is the
 * harness model: what the server's gate decides (no second code within the time a
 * sign-in by code counts for, the password first for an account with one) is the
 * xUnit suite's (RegisterPasskeyCommandHandlerTests); what is proven here is what
 * only a built bundle in a browser decides: the screens, the window and the device
 * in the order the model's gate asks, and one resume of the application's request.
 * A passkey sign-in opens no offer: the WAYS_BACK legs above end at the callback.
 *
 * Deliberate break this section must catch: the offer left with
 * `endsSignIn: false` (no marker, so the bounce for set-up asks for a second
 * sign-in).
 */
interface OfferServer {
  resumes: string[]
  logins: number
  codeSignIns: number
  meReads: number
  registrationOptions: number
  registrationCodeSends: number
  registered: Registered[]
  passkeySignIns: number
  windowProofs: number
  passkeyProofs: Asserted[]
  passwordProved: boolean
}

const newOfferServer = (): OfferServer => ({
  resumes: [],
  logins: 0,
  codeSignIns: 0,
  meReads: 0,
  registrationOptions: 0,
  registrationCodeSends: 0,
  registered: [],
  passkeySignIns: 0,
  windowProofs: 0,
  passkeyProofs: [],
  passwordProved: false,
})

const PERSON = {
  id: "28028028-0280-0280-0280-280280280280",
  email: "person@example.test",
  firstName: "Pat",
  lastName: "Person",
  displayName: "Pat Person",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

/** The deployment's options on the sign-in page: code first, and passkeys may run. */
const CODE_FIRST_WITH_PASSKEYS = {
  platformName: "AuthSystem",
  staySignedInOffered: true,
  codeSignInOffered: true,
  primaryMethod: "code",
  passkeySignInOffered: true,
}

/** The same with the password first: the address, then the password (OI-280 PR A). */
const PASSWORD_FIRST_WITH_PASSKEYS = {
  platformName: "AuthSystem",
  staySignedInOffered: true,
  passkeySignInOffered: true,
}

function registrationOptions(emailCodeRequired: boolean) {
  return {
    options: {
      rp: { id: PASSKEY_RP_ID, name: "AuthSystem" },
      user: { id: USER_HANDLE, name: PERSON.email, displayName: PERSON.displayName },
      challenge: challenge(),
      pubKeyCredParams: [
        { type: "public-key", alg: -7 },
        { type: "public-key", alg: -257 },
      ],
      timeout: 300_000,
      attestation: "none",
      excludeCredentials: [],
      authenticatorSelection: { residentKey: "required", requireResidentKey: true, userVerification: "required" },
    },
    expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
    emailCodeRequired,
  }
}

/**
 * The journey's server. The registration's gate is modelled as the server decides
 * it right after a sign-in: a password account not yet proved by its password is
 * asked for it first (the window), then no code; an account without one goes to
 * the device; `codeAfterPassword` models a password sign-in on an account with no
 * factor, which the gate asks for the registration's code. The resume is answered
 * with the application's callback, or once with `bounce`, as the server answers a
 * session short of the application's second step.
 */
async function serveOffer(
  api: HarnessApi,
  state: OfferServer,
  landing: string,
  account: { hasPassword: boolean; codeAfterPassword?: boolean; bounce?: string },
  branding: Record<string, unknown>
) {
  await api.useAuthenticated(
    [],
    async (route: Route, url: URL) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path === "/api/v1/auth/authorize/resume" && method === "GET") {
        state.resumes.push(url.search)
        const location =
          account.bounce && state.resumes.length === 1
            ? `${ORIGINS.accounts}/login?${PENDING}${account.bounce}`
            : landing
        await route.fulfill({ status: 302, headers: { location } })
        return true
      }
      if (path === "/api/v1/auth/passkeys/assertion/options") {
        await fulfillJson(route, assertionOptions())
        return true
      }
      if (path === "/api/v1/auth/passkeys/lookup") {
        await fulfillJson(route, { passkey: false })
        return true
      }
      if (path === "/api/v1/auth/passkeys/assertion/verify" && method === "POST") {
        state.passkeySignIns += 1
        await api.firstParty.answerSignIn(route, PERSON, "oi280b-secret")
        return true
      }
      if (path === "/api/v1/auth/email-code/start" && method === "POST") {
        await fulfillJson(route, {
          pendingId: "harness-pending",
          maskedEmail: "p****n@example.test",
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/email-code/verify" && method === "POST") {
        await fulfillJson(route, { step: "signIn" })
        return true
      }
      if (path === "/api/v1/auth/email-code/sign-in" && method === "POST") {
        state.codeSignIns += 1
        await api.firstParty.answerSignIn(route, PERSON, "oi280b-secret")
        return true
      }
      if (path === "/api/v1/auth/login" && method === "POST") {
        state.logins += 1
        state.passwordProved = true
        await api.firstParty.answerSignIn(route, PERSON, "oi280b-secret")
        return true
      }
      if (path === "/api/v1/users/me" && method === "GET") {
        state.meReads += 1
        await fulfillJson(route, {
          ...PERSON,
          hasPassword: account.hasPassword,
          emailConfirmed: true,
          phoneConfirmed: false,
          twoFactorEnabled: false,
          totpEnabled: false,
          emailCodeEnabled: false,
          qualifyingMfa: state.registered.length > 0,
          passkeyCount: state.registered.length,
          emailCodeAvailable: true,
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/options" && method === "POST") {
        state.registrationOptions += 1
        if (account.hasPassword && !state.passwordProved) {
          await route.fulfill({
            status: 403,
            contentType: "application/problem+json",
            body: JSON.stringify({
              status: 403,
              code: "Auth.ReauthenticationRequired",
              detail: "Sign in again to continue.",
            }),
          })
          return true
        }
        await fulfillJson(route, registrationOptions(account.codeAfterPassword === true))
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/email-code" && method === "POST") {
        state.registrationCodeSends += 1
        await fulfillJson(route, {
          emailCodeRequired: true,
          sentTo: "p****n@example.test",
          expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/verify" && method === "POST") {
        const body = route.request().postDataJSON() as { credential: { id: string }; name: string | null }
        const signed = clientData(body.credential)
        const passkey = {
          credentialId: body.credential.id,
          name: body.name ?? "Passkey",
          origin: signed.origin,
          type: signed.type,
          fmt: attestationFormat(body.credential),
        }
        state.registered.push(passkey)
        await fulfillJson(route, passkeyDto(passkey))
        return true
      }
      // The "Confirm it's you" window's password (its options are the defaults').
      if (path === "/api/v1/auth/reauthentication" && method === "POST") {
        state.windowProofs += 1
        state.passwordProved = true
        await route.fulfill({ status: 204, body: "" })
        return true
      }
      // The new passkey, used once in place (T-B8, D-B5).
      if (path === "/api/v1/auth/reauthentication/passkey" && method === "POST") {
        const body = route.request().postDataJSON() as { credential: { id: string } }
        const signed = clientData(body.credential)
        state.passkeyProofs.push({ credentialId: body.credential.id, origin: signed.origin, type: signed.type })
        await route.fulfill({ status: 204, body: "" })
        return true
      }
      return false
    },
    { session: "none", branding }
  )
}

/** The code sign-in on the accounts page: the address, "Continue with email", the six digits. */
async function signInByCode(page: Page) {
  await expect(page.getByRole("button", { name: en.auth.continueWithEmail })).toBeVisible(FIRST_LOAD)
  await page.locator('input[type="email"]').fill(PERSON.email)
  await page.getByRole("button", { name: en.auth.continueWithEmail }).click()
  await page.locator("[data-slot=input-otp]").click()
  await page.keyboard.type("123456")
}

/** The password sign-in, address first (passkeys may run): the address, Continue, the password. */
async function signInByPassword(page: Page) {
  await page.locator('input[type="email"]').fill(PERSON.email)
  await page.getByRole("button", { name: "Continue", exact: true }).click(FIRST_LOAD)
  await page.locator('input[type="password"]').fill("Harness-Password-1!")
  await page.locator('button[type="submit"]').click()
}

const offerHeading = (page: Page) => page.getByRole("heading", { name: en.auth.passkeyOffer.title })

function theApplication(attacker: AttackerPages) {
  return `${attacker.serve(HOSTS.linkedApp, "/callback", "<!doctype html><title>The application</title>")}?code=harness-code`
}

test("oi280b: the offer after a password account's sign-in by code: the password in the window, then the device, no second code, one resume", async ({
  page,
  api,
  attacker,
}) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(api, state, landing, { hasPassword: true }, CODE_FIRST_WITH_PASSKEYS)

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByCode(page)

  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  expect(state.resumes).toEqual([])
  await page.getByRole("button", { name: en.auth.passkeyOffer.add }).click()

  // The gate asks a password account signed in by code for its password, in place.
  const proof = page.getByRole("dialog", { name: en.auth.reauthenticate.title })
  await proof.getByLabel(en.auth.password).fill("Harness-Password-1!")
  await proof.getByRole("button", { name: en.auth.reauthenticate.confirm, exact: true }).click()

  // Then the device, with no emailed code: the addition's dialog asks for none.
  const dialog = page.getByRole("dialog", { name: en.profile.passkeys.addTitle })
  await expect(dialog).toBeVisible(FIRST_LOAD)
  await expect(dialog.getByRole("button", { name: en.auth.emailMeACode })).toHaveCount(0)
  await dialog.getByRole("button", { name: en.profile.passkeys.create }).click()

  await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.windowProofs).toBe(1)
  expect(state.registrationCodeSends).toBe(0)
  expect(state.registered).toEqual([
    expect.objectContaining({ origin: ORIGINS.accounts, type: "webauthn.create" }),
  ])
})

test("oi280b: the offer after a passwordless account's sign-in by code: «add» goes straight to the device", async ({
  page,
  api,
  attacker,
}) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(api, state, landing, { hasPassword: false }, CODE_FIRST_WITH_PASSKEYS)

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByCode(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.add }).click()
  const dialog = page.getByRole("dialog", { name: en.profile.passkeys.addTitle })
  await dialog.getByRole("button", { name: en.profile.passkeys.create }).click()

  await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.windowProofs).toBe(0)
  expect(state.registrationCodeSends).toBe(0)
  expect(state.registered).toHaveLength(1)
})

/**
 * A sign-in with no application's request (the accounts app's own): the sign-in pages' guard
 * wakes as the session flips, while the offer's lazy route is still loading, and races the page's
 * completion; it must follow the completion to the offer (require-auth.tsx), or the person lands
 * on the home page with no offer, as the pictures' run found. Deliberate break, verified red: the
 * guard's follow removed.
 */
test("oi280b: a sign-in with no application's request gets the offer too, and «Not now» lands where it was going", async ({
  page,
  api,
}) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  await serveOffer(api, state, `${ORIGINS.accounts}/never`, { hasPassword: true }, CODE_FIRST_WITH_PASSKEYS)

  await page.goto(`${ORIGINS.accounts}/login`)
  await signInByCode(page)

  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  expect(new URL(page.url()).pathname).toBe("/passkey/offer")
  await page.getByRole("button", { name: en.auth.passkeyOffer.notNow }).click()

  await expect.poll(() => new URL(page.url()).pathname, FIRST_LOAD).toBe("/")
  expect(state.resumes).toEqual([])
  expect(state.codeSignIns).toBe(1)
})

/**
 * REVIEW (OI-280 PR B) finding 1: signed out from the offer's window («Can't confirm it here?»,
 * with no application's request to keep), the session that ended on the offer records it as where
 * the person was going. The next sign-in's offer must lead on to the landing, never back to itself,
 * where nothing could leave again. Deliberate break, verified red: the offer taken as a destination.
 */
test("oi280b: signed out from the offer's window and in again, the offer still leads on, never back to itself", async ({
  page,
  api,
}) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  await serveOffer(api, state, `${ORIGINS.accounts}/never`, { hasPassword: true }, CODE_FIRST_WITH_PASSKEYS)

  await page.goto(`${ORIGINS.accounts}/login`)
  await signInByCode(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.add }).click()

  // The window asks for the password; its way out signs out and back in.
  const proof = page.getByRole("dialog", { name: en.auth.reauthenticate.title })
  await proof.getByRole("button", { name: en.auth.reauthenticate.signOutLink }).click()
  const again = page.getByRole("dialog", { name: en.auth.reauthenticateTitle })
  await again.getByRole("button", { name: en.auth.reauthenticateAction }).click()
  await expect.poll(() => new URL(page.url()).pathname, FIRST_LOAD).toBe("/login")

  await signInByCode(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.notNow }).click()

  await expect.poll(() => new URL(page.url()).pathname, FIRST_LOAD).toBe("/")
  expect(state.codeSignIns).toBe(2)
  expect(state.resumes).toEqual([])
})

test("oi280b: «Not now» returns to the application at once, and stores nothing", async ({ page, api, attacker }) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(api, state, landing, { hasPassword: true }, CODE_FIRST_WITH_PASSKEYS)

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByCode(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.notNow }).click()

  await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.registrationOptions).toBe(0)
  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  expect(await page.evaluate(() => localStorage.getItem("auth.passkeyOffer.off"))).toBeNull()
})

test("oi280b: «Don't ask on this device»: the next sign-in on this browser has no offer", async ({ page, api, attacker }) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(api, state, landing, { hasPassword: true }, CODE_FIRST_WITH_PASSKEYS)

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByCode(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.dontAsk }).click()
  await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
  expect(state.meReads).toBe(1)

  // The application sends the person to sign in again on this browser.
  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByCode(page)

  await expect.poll(() => state.resumes, FIRST_LOAD).toEqual([`?${PENDING}`, `?${PENDING}`])
  await expect.poll(() => page.url()).toBe(landing)
  // The offer's page never opened again: it reads the account before anything else.
  expect(state.meReads).toBe(1)
  expect(state.codeSignIns).toBe(2)
})

/**
 * «Add» before an application's two-step requirement (REVIEW 160 F6, D-160-3 (a)): a registration
 * raises no session (D-B5), so the resume comes back to the sign-in page, whose passkey button
 * reaches the application, and the sign-in that follows says it was a passkey's: no second offer.
 * Deliberate break, verified red: the passkey sign-in no longer says so (`signedInWithPasskey`
 * dropped in login.tsx), and the account is read again for an offer.
 */
test("oi280b: «add» before an application's two-step requirement: the resume comes back, the passkey button goes on, no second offer", async ({
  page,
  api,
  attacker,
}) => {
  await attachVirtualAuthenticator(page)
  await withoutSuggestion(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(
    api,
    state,
    landing,
    { hasPassword: false, bounce: "&reason=mfa_passkey&method=password" },
    CODE_FIRST_WITH_PASSKEYS
  )

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByCode(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.add }).click()
  await page
    .getByRole("dialog", { name: en.profile.passkeys.addTitle })
    .getByRole("button", { name: en.profile.passkeys.create })
    .click()

  const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: en.auth.passkey.signIn })
  await expect(passkey).toBeEnabled(FIRST_LOAD)
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.registered).toHaveLength(1)
  const readsBeforeThePasskey = state.meReads
  await passkey.click()

  await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
  expect(state.passkeySignIns).toBe(1)
  expect(state.resumes).toEqual([`?${PENDING}`, `?${PENDING}`])
  expect(state.meReads).toBe(readsBeforeThePasskey)
})

test("oi280b: leaving the offer leaves the sign-in's marker: the bounce for set-up goes straight to the set-up page", async ({
  page,
  api,
  attacker,
}) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(
    api,
    state,
    landing,
    { hasPassword: true, bounce: "&reason=mfa_enroll&method=password" },
    PASSWORD_FIRST_WITH_PASSKEYS
  )

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await signInByPassword(page)
  await expect(offerHeading(page)).toBeVisible(FIRST_LOAD)
  await page.getByRole("button", { name: en.auth.passkeyOffer.notNow }).click()

  // OI-275: the marker the offer's way out wrote spares the second sign-in.
  await expect(page).toHaveURL(`${ORIGINS.accounts}/two-factor/required`, FIRST_LOAD)
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.logins).toBe(1)
})

test("oi280b: the set-up page offers a passkey beside the app; added, then used once in place, the request resumes once", async ({
  page,
  api,
  attacker,
}) => {
  await attachVirtualAuthenticator(page)
  const state = newOfferServer()
  const landing = theApplication(attacker)
  await serveOffer(
    api,
    state,
    landing,
    { hasPassword: true, codeAfterPassword: true },
    PASSWORD_FIRST_WITH_PASSKEYS
  )

  // The application requires two steps and the account has none: the set-up page.
  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_enroll&method=password`)
  await signInByPassword(page)
  await expect(page).toHaveURL(`${ORIGINS.accounts}/two-factor/required`, FIRST_LOAD)
  await expect(page.getByText(en.auth.mfaAppRequiredSubtitleWithPasskey)).toBeVisible()
  await expect(page.getByRole("button", { name: en.profile.enableTwoFactor })).toBeVisible()
  // The set-up page comes first, and no offer follows it.
  await expect(offerHeading(page)).toHaveCount(0)

  await page.getByRole("button", { name: en.auth.mfaRequiredAddPasskey }).click()
  const dialog = page.getByRole("dialog", { name: en.profile.passkeys.addTitle })
  // A password sign-in proved no mailbox: the registration's code (X02).
  await dialog.getByRole("button", { name: en.auth.emailMeACode }).click()
  await expect.poll(() => state.registrationCodeSends).toBe(1)
  await dialog.locator("[data-slot=input-otp]").click()
  await page.keyboard.type("654321")
  await dialog.getByRole("button", { name: en.profile.passkeys.create }).click()

  // Added, not used: the registration raised no session (D-B5). One touch, in place.
  const useIt = page.getByTestId("passkey-sign-in").getByRole("button", { name: en.auth.mfaRequiredUsePasskeyAction })
  await expect(useIt).toBeEnabled(FIRST_LOAD)
  expect(state.resumes).toEqual([])
  await useIt.click()

  await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.passkeyProofs).toEqual([
    { credentialId: state.registered[0].credentialId, origin: ORIGINS.accounts, type: "webauthn.get" },
  ])
})
