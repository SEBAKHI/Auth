import { HOSTS, ORIGINS, expect, test } from "./fixtures"
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

  // The passkey screen's button, not the address field's suggestion: on this page
  // the browser says it offers no suggestion, so the button alone signs in (T-C5).
  // The suggestion has its own test below. OI-280: no button for every visitor;
  // the address and "Continue" lead to the passkey screen.
  await withoutSuggestion(page)
  await page.goto(`${ORIGINS.accounts}/login`)
  await expect(page.getByLabel("Email")).toHaveAttribute(
    "autocomplete",
    "username"
  )
  await page.getByLabel("Email").fill(USER.email)
  await page.getByRole("button", { name: "Continue", exact: true }).click(FIRST_LOAD)
  const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with a passkey" })
  await expect(passkey).toBeEnabled(FIRST_LOAD)
  await expect(passkey).toBeFocused()
  await passkey.click()

  await expect.poll(() => asserted.length).toBe(1)
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

// OI-280 (V11): the button way is the address, "Continue", the passkey screen and
// its button; the suggestion and the leading button under mfa_passkey are as they
// were. Deliberate break, verified red: the page goes to the passkey screen
// without asking (every address taken for one that has a passkey).
const WAYS_BACK = [
  {
    name: "the passkey screen's button, after the address",
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

    await api.useAnonymous(
      async (route, url) => {
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
      await expect(
        page.getByRole("heading", { name: "Use your passkey" })
      ).toBeVisible(FIRST_LOAD)
    }
    if (way.way !== "suggestion") {
      const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with a passkey" })
      await expect(passkey).toBeEnabled(FIRST_LOAD)
      await passkey.click()
    }

    await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
    expect(asserted).toEqual([{ credentialId, origin: ORIGINS.accounts, type: "webauthn.get" }])
    // One navigation, carrying the request and nothing of the sign-in's words.
    expect(resumes).toEqual([`?${PENDING}`])
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
