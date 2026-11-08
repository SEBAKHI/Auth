import { HOSTS, ORIGINS, expect, test } from "./fixtures"
import { fulfillJson } from "../isolated/mock-authenticated-api"
import { loginResponse } from "../isolated/accounts/mock-anonymous-api"
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

/**
 * The accounts app's anonymous model: passkey options, and a sign-in that records
 * what was signed. `hold` keeps the sign-in's answer back until the test lets it
 * go, so the page is still there to be read.
 */
function anonymousPasskeyModel(asserted: Asserted[], hold: Promise<void> = Promise.resolve()) {
  return async (route: import("@playwright/test").Route, url: URL) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/passkeys/assertion/options") {
      await fulfillJson(route, assertionOptions())
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
  await api.useAnonymous(anonymousPasskeyModel(asserted))

  // The button, not the address field's suggestion: on this page the browser
  // says it offers no suggestion, so the button alone signs in (T-C5). The
  // suggestion has its own test below.
  await withoutSuggestion(page)
  await page.goto(`${ORIGINS.accounts}/login`)
  const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with a passkey" })
  await expect(passkey).toBeEnabled(FIRST_LOAD)
  await expect(page.getByLabel("Email")).toHaveAttribute("autocomplete", "username")
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
 * for a signed-in browser is not proven here: FirstPartySignInExitCoverageTests
 * holds that the verify endpoint delivers its session through the filter that
 * writes the IdP cookie, and no real run has yet gone from an application through
 * a passkey and back (PasskeyDevRun has no such leg).
 */
const HANDLE = "0123456789abcdef".repeat(4)
const PENDING = `client_id=CRM&auth_request=${HANDLE}`

const WAYS_BACK = [
  {
    name: "the button",
    query: PENDING,
    suggestion: false,
  },
  {
    name: "the address field's suggestion",
    query: PENDING,
    suggestion: true,
  },
  {
    name: "the button, when the application asks for the passkey (mfa_passkey)",
    query: `${PENDING}&reason=mfa_passkey&method=password`,
    suggestion: false,
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
    const passkeyModel = anonymousPasskeyModel(asserted)
    const callback = attacker.serve(HOSTS.linkedApp, "/callback", "<!doctype html><title>The application</title>")
    const landing = `${callback}?code=harness-code&state=harness-state`

    await api.useAnonymous(async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/auth/authorize/resume" && route.request().method() === "GET") {
        resumes.push(url.search)
        await route.fulfill({ status: 302, headers: { location: landing } })
        return true
      }
      return passkeyModel(route, url)
    })

    if (!way.suggestion) await withoutSuggestion(page)
    await page.goto(`${ORIGINS.accounts}/login?${way.query}`)

    if (!way.suggestion) {
      const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with a passkey" })
      await expect(passkey).toBeEnabled(FIRST_LOAD)
      await passkey.click()
    }

    await expect.poll(() => page.url(), FIRST_LOAD).toBe(landing)
    expect(asserted).toEqual([{ credentialId, origin: ORIGINS.accounts, type: "webauthn.get" }])
    // One navigation, carrying the request and nothing of the sign-in's words.
    expect(resumes).toEqual([`?${PENDING}`])
  })
}

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
