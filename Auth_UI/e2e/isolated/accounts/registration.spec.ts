import { expect, test, type Page } from "@playwright/test"

import {
  fulfillJson,
  installAnonymousApi,
  loginResponse,
  type SeenRequest,
} from "./mock-anonymous-api"
import { fulfillProblem, problem } from "../problem"

/**
 * Verify-first sign-up, walked in a real browser against the production build.
 *
 * The unit tests prove each screen in isolation with the router and the store
 * stubbed. What only a browser can show is the part between the screens: that
 * the identity of a pending sign-up survives a reload of the code screen while
 * the code itself does not survive a reload of the password screen, that the
 * three request bodies are what the server contract says and nothing more,
 * that the address the person typed comes back read-only where a password
 * manager will pair it with the new password, and that the sign-in the last
 * request answers with actually lands the person inside the app.
 *
 * Every API call is fulfilled here, and every non-GET request is recorded, so
 * the assertions are about what was sent as much as what was rendered.
 */

const AUTHORIZE =
  "https://auth.example.com/api/v1/auth/authorize?client_id=app&state=xyz"
const RETURN_TO = `?returnTo=${encodeURIComponent(AUTHORIZE)}`

// OI-145: what the authorize endpoint sends now. The accounts pages carry the
// client and a 64-hex handle to the request the server stored, and the app
// resumes it at the API's resume endpoint.
const HANDLE = "0123456789abcdef".repeat(4)
const HANDLE_QUERY = `?client_id=app&auth_request=${HANDLE}`
const RESUME = `https://auth.example.com/api/v1/auth/authorize/resume${HANDLE_QUERY}`

/** The two shapes a pending request reaches the accounts pages in. */
const SHAPES = [
  { name: "a stored request (client_id and its handle)", query: HANDLE_QUERY, resumes: RESUME, handle: true },
  // The one legacy walk: links in flight during the deploy, and the server's
  // switch turned off, still carry the whole authorize URL.
  { name: "the legacy returnTo", query: RETURN_TO, resumes: AUTHORIZE, handle: false },
] as const

/**
 * No query value on the accounts origin holds an address: a value that looks
 * like an off-site URL is what a web application firewall refuses.
 */
function expectNoAddressInTheQuery(page: Page) {
  const url = new URL(page.url())
  if (url.origin !== new URL("http://localhost:4176").origin) return
  const values = [...url.searchParams.values()]
  expect(values.length, `${url.pathname} carries the request`).toBeGreaterThan(0)
  expect(values.filter((value) => value.includes("://"))).toEqual([])
}

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@one.example",
  firstName: "Jane",
  lastName: "Doe",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
  emailConfirmed: true,
}

interface SignUpApiOptions {
  /** How long the code the start step issues stays valid. */
  codeLifetimeMs?: number
  /** A ProblemDetails body to refuse the completion with, instead of a session. */
  refuseCompletionWith?: ReturnType<typeof problem>
  /** The public branding of client `app`, the one AUTHORIZE names. */
  appBranding?: { name: string; logoUrl: string }
  /** Every public-branding request, in order, so a test can count them. */
  brandingRequests?: string[]
  /** Every top-level navigation back to the API (resume or authorize), in order. */
  landed?: string[]
}

/** The three sign-up endpoints, plus what the signed-in shell asks for after. */
async function installSignUpApi(
  page: Page,
  seen: SeenRequest[],
  options: SignUpApiOptions = {}
) {
  await installAnonymousApi(
    page,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (
        options.appBranding &&
        path === "/api/v1/applications/app/public-branding"
      ) {
        options.brandingRequests?.push(path)
        await fulfillJson(route, options.appBranding)
        return true
      }
      if (path === "/api/v1/auth/forgot-password") {
        await fulfillJson(route, {
          maskedEmail: "j***@one.example",
          expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/registration/start") {
        await fulfillJson(route, {
          pendingId: "handle-1",
          maskedEmail: "j***@one.example",
          expiresAt: new Date(
            Date.now() + (options.codeLifetimeMs ?? 5 * 60_000)
          ).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/registration/verify") {
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === "/api/v1/auth/registration/complete") {
        if (options.refuseCompletionWith) {
          await fulfillProblem(route, options.refuseCompletionWith)
          return true
        }
        await fulfillJson(route, loginResponse(USER))
        return true
      }
      if (path === "/api/v1/auth/me") {
        await fulfillJson(route, USER)
        return true
      }
      if (path === "/api/v1/auth/refresh") {
        await fulfillJson(route, loginResponse(USER).token)
        return true
      }
      if (
        options.landed &&
        (path === "/api/v1/auth/authorize/resume" || path === "/api/v1/auth/authorize")
      ) {
        // The browser went back to the API: stop here.
        options.landed.push(url.toString())
        await route.fulfill({ status: 200, contentType: "text/html", body: "<p>authorize</p>" })
        return true
      }
      return false
    },
    { seen }
  )
}

const requestsTo = (seen: SeenRequest[], step: string) =>
  seen.filter((request) => request.path.endsWith(`/registration/${step}`))

async function typeCode(page: Page, code: string) {
  await page.locator("[data-slot=input-otp]").click()
  await page.keyboard.type(code)
}

async function fillNameAndPassword(page: Page) {
  await page.getByLabel("First name").fill("Jane")
  await page.getByLabel("Last name").fill("Doe")
  await page.getByLabel("Password", { exact: true }).fill("NewPass1!")
}

/**
 * From the sign-in page the authorize redirect lands on, through its sign-up
 * link: email, the code, then the name and password, reloading each of the
 * last two screens, and back to the API with the same request.
 */
async function signUpWalk(page: Page, shape: (typeof SHAPES)[number]) {
  const seen: SeenRequest[] = []
  const landed: string[] = []
  await installSignUpApi(page, seen, { landed })
  const RETURN_TO = shape.query
  const check = () => {
    if (shape.handle) expectNoAddressInTheQuery(page)
  }

  // The authorize redirect lands on sign-in; the sign-up link keeps the request.
  await page.goto(`/login${RETURN_TO}`)
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible()
  check()
  await page.getByRole("link", { name: "Sign up" }).click()
  await page.waitForURL(`**/register${RETURN_TO}`)
  check()

  // The first screen asks for the address and nothing else, and its sign-in
  // link keeps the pending authorize request.
  await expect(
    page.getByRole("heading", { name: "Create your account" })
  ).toBeVisible()
  await expect(page.getByRole("textbox")).toHaveCount(1)
  await expect(page.getByLabel("Password", { exact: true })).toHaveCount(0)
  await expect(page.getByRole("link", { name: "Sign in" })).toHaveAttribute(
    "href",
    `/login${RETURN_TO}`
  )

  await page.getByLabel("Email").fill("jane@one.example")
  await page.getByRole("button", { name: "Send code" }).click()
  await page.waitForURL(`**/register/verify${RETURN_TO}`)
  check()
  expect(requestsTo(seen, "start").map((request) => request.body)).toEqual([
    { email: "jane@one.example", preferredLanguage: "en" },
  ])

  // The code screen: the masked address, a live countdown, no request of its
  // own, and a field a phone recognises as a one-time code.
  await expect(page.getByText("j***@one.example")).toBeVisible()
  await expect(page.getByText(/Code expires in 0[45]:/)).toBeVisible()
  const otp = page.locator("input[data-input-otp]")
  await expect(otp).toHaveAttribute("autocomplete", "one-time-code")
  await expect(otp).toHaveAttribute("inputmode", "numeric")
  await expect(
    page.getByRole("button", { name: "Send a new code" })
  ).toBeDisabled()

  // A reload keeps the identity in the tab and asks the server for nothing.
  await page.reload()
  await page.waitForURL(`**/register/verify${RETURN_TO}`)
  await expect(page.getByText("j***@one.example")).toBeVisible()
  expect(requestsTo(seen, "start")).toHaveLength(1)
  expect(requestsTo(seen, "verify")).toHaveLength(0)

  await typeCode(page, "123456")
  await page.waitForURL(`**/register/complete${RETURN_TO}`)
  check()
  expect(requestsTo(seen, "verify").map((request) => request.body)).toEqual([
    { pendingId: "handle-1", otp: "123456" },
  ])

  // The address comes back real and read-only, in the field a password
  // manager pairs the new password with; there is no confirm field.
  await expect(
    page.getByRole("heading", { name: "Set up your account" })
  ).toBeVisible()
  const email = page.getByLabel("Email")
  await expect(email).toHaveValue("jane@one.example")
  await expect(email).toHaveAttribute("readonly", "")
  await expect(email).toHaveAttribute("autocomplete", "username")
  await expect(page.getByLabel(/confirm/i)).toHaveCount(0)
  await expect(page.getByRole("link", { name: "Change email" })).toHaveAttribute(
    "href",
    `/register${RETURN_TO}`
  )

  // A reload here forgets the code - it lives in memory only - and returns to
  // the code screen, where the identity is still known.
  await page.reload()
  await page.waitForURL(`**/register/verify${RETURN_TO}`)
  await expect(page.getByText("j***@one.example")).toBeVisible()
  await typeCode(page, "123456")
  await page.waitForURL(`**/register/complete${RETURN_TO}`)

  await fillNameAndPassword(page)
  const password = page.getByLabel("Password", { exact: true })
  await expect(password).toHaveAttribute("type", "password")
  await page.getByRole("button", { name: "Show password" }).click()
  await expect(password).toHaveAttribute("type", "text")
  await page.getByRole("button", { name: "Hide password" }).click()
  await expect(password).toHaveAttribute("type", "password")

  await page.getByRole("button", { name: "Create account" }).click()
  // With a pending authorize request the completion leaves for the API
  // origin, where the suite answers the one navigation it expects.
  await expect.poll(() => landed.length).toBeGreaterThan(0)
  expect(landed).toEqual([shape.resumes])
  await page.waitForURL(shape.resumes)

  const completions = requestsTo(seen, "complete")
  expect(completions).toHaveLength(1)
  const body = completions[0].body as Record<string, unknown>
  expect(body).not.toHaveProperty("email")
  expect(body).toMatchObject({
    pendingId: "handle-1",
    otp: "123456",
    password: "NewPass1!",
    firstName: "Jane",
    lastName: "Doe",
  })
  expect(Object.keys(body).sort()).toEqual([
    "firstName",
    "lastName",
    "otp",
    "password",
    "pendingId",
    "timeZone",
  ])
}

for (const shape of SHAPES) {
  test(`sign-in, then sign-up, reloading the code and password screens - ${shape.name}`, ({
    page,
  }) => signUpWalk(page, shape))
}

test("a plain sign-up ends signed in on the profile, with nothing of the flow left behind", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSignUpApi(page, seen)

  await page.goto("/register")
  await page.getByLabel("Email").fill("jane@one.example")
  await page.getByRole("button", { name: "Send code" }).click()
  await page.waitForURL("**/register/verify")
  await typeCode(page, "123456")
  await page.waitForURL("**/register/complete")
  await fillNameAndPassword(page)
  await page.getByRole("button", { name: "Create account" }).click()

  await page.waitForURL("**/profile")
  await expect(page.getByText("Your account is ready.")).toBeVisible()

  const storage = await page.evaluate(() => ({
    pending: sessionStorage.getItem("auth.registration.pending"),
    refresh: localStorage.getItem("auth.refreshToken"),
  }))
  expect(storage.pending).toBeNull()
  expect(storage.refresh).toBe("isolated-refresh")
})

test("a code refused at the last step returns to the code screen with the reason, and does not resubmit", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSignUpApi(page, seen, {
    refuseCompletionWith: problem(400, "EmailVerification.InvalidOrExpiredOtp", {
      detail: "The code is wrong or has expired.",
    }),
  })

  await page.goto("/register")
  await page.getByLabel("Email").fill("jane@one.example")
  await page.getByRole("button", { name: "Send code" }).click()
  await page.waitForURL("**/register/verify")
  await typeCode(page, "123456")
  await page.waitForURL("**/register/complete")
  await fillNameAndPassword(page)
  await page.getByRole("button", { name: "Create account" }).click()

  await page.waitForURL("**/register/verify")
  await expect(page.getByRole("alert")).toHaveText(
    "The code is wrong or has expired."
  )
  expect(requestsTo(seen, "complete")).toHaveLength(1)
  // The identity survived the refusal: the same sign-up continues from here.
  await expect(page.getByText("j***@one.example")).toBeVisible()
})

test("a code the browser's clock calls expired is still sent - the server decides", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSignUpApi(page, seen, { codeLifetimeMs: 1_500 })

  await page.goto("/register")
  await page.getByLabel("Email").fill("jane@one.example")
  await page.getByRole("button", { name: "Send code" }).click()
  await page.waitForURL("**/register/verify")
  await expect(page.getByText(/code has expired/i)).toBeVisible({
    timeout: 10_000,
  })
  await expect(
    page.getByRole("button", { name: "Send a new code" })
  ).toBeEnabled()

  await typeCode(page, "123456")

  await page.waitForURL("**/register/complete")
  expect(requestsTo(seen, "verify")).toHaveLength(1)
})

test("the three screens render right-to-left in Arabic with their own copy", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSignUpApi(page, seen)
  await page.addInitScript(() => localStorage.setItem("auth.language", "ar"))

  await page.goto("/register")
  await expect(page.getByRole("heading", { name: "أنشئ حسابك" })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.dir)).toBe("rtl")

  await page.getByRole("textbox").fill("jane@one.example")
  await page.getByRole("button", { name: "إرسال الرمز" }).click()
  await page.waitForURL("**/register/verify")
  await typeCode(page, "123456")
  await page.waitForURL("**/register/complete")
  await expect(page.getByRole("heading", { name: "إعداد حسابك" })).toBeVisible()
  await expect(
    page.getByRole("button", { name: "إظهار كلمة المرور" })
  ).toBeVisible()
  // The address itself stays left-to-right inside the right-to-left form.
  await expect(page.getByRole("textbox", { name: "البريد الإلكتروني" })).toHaveAttribute(
    "dir",
    "ltr"
  )
})

/** The application behind AUTHORIZE, as its public-branding endpoint answers. */
const APP = {
  name: "Partner App",
  logoUrl: "https://auth.example.com/uploads/images/partner-app.png",
}
// A 1x1 PNG: enough for the browser to decode, so "loaded" can be measured.
const PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=",
  "base64"
)

async function serveAppLogo(page: Page) {
  await page.route(APP.logoUrl, (route) =>
    route.fulfill({ status: 200, contentType: "image/png", body: PNG })
  )
}

/** The application's logo, decoded rather than a broken image, and the trust marker. */
async function expectAppHeader(page: Page) {
  const logo = page.getByRole("img", { name: APP.name })
  await expect(logo).toBeVisible()
  await expect(logo).toHaveAttribute("src", APP.logoUrl)
  await expect
    .poll(() =>
      logo.evaluate((img: HTMLImageElement) =>
        img.complete ? img.naturalWidth : 0
      )
    )
    .toBeGreaterThan(0)
  await expect(page.getByText("Secured by AuthSystem")).toBeVisible()
}

test("a visitor an application sent here sees that application on every screen of sign-up", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const brandingRequests: string[] = []
  await installSignUpApi(page, seen, { appBranding: APP, brandingRequests })
  await serveAppLogo(page)
  const RETURN_TO = HANDLE_QUERY

  await page.goto(`/login${RETURN_TO}`)
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible()
  await expectAppHeader(page)

  await page.getByRole("link", { name: "Sign up" }).click()
  await page.waitForURL(`**/register${RETURN_TO}`)
  await expect(
    page.getByRole("heading", { name: "Create your account" })
  ).toBeVisible()
  await expectAppHeader(page)

  await page.getByLabel("Email").fill("jane@one.example")
  await page.getByRole("button", { name: "Send code" }).click()
  await page.waitForURL(`**/register/verify${RETURN_TO}`)
  await expect(page.getByText("j***@one.example")).toBeVisible()
  await expectAppHeader(page)

  await typeCode(page, "123456")
  await page.waitForURL(`**/register/complete${RETURN_TO}`)
  await expect(
    page.getByRole("heading", { name: "Set up your account" })
  ).toBeVisible()
  await expectAppHeader(page)

  // One request for the whole walk: the screens share the answer, which is
  // what keeps the platform mark from flashing in at every step.
  expect(brandingRequests).toHaveLength(1)
})

test("the forgot-password screen keeps the application, and its way back keeps the request", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSignUpApi(page, seen, { appBranding: APP })
  await serveAppLogo(page)
  const RETURN_TO = HANDLE_QUERY

  await page.goto(`/login${RETURN_TO}`)
  await expectAppHeader(page)
  expectNoAddressInTheQuery(page)
  await page.getByRole("link", { name: "Forgot password?" }).click()
  await page.waitForURL(`**/forgot-password${RETURN_TO}`)
  expectNoAddressInTheQuery(page)
  await expect(
    page.getByRole("heading", { name: "Reset your password" })
  ).toBeVisible()
  await expectAppHeader(page)
  await expect(page.getByRole("link", { name: "Back to sign in" })).toHaveAttribute(
    "href",
    `/login${RETURN_TO}`
  )

  // The mail carries no trace of the application: the request is the address
  // alone, so the reset page its link opens stays the platform's.
  await page.getByLabel("Email").fill("jane@one.example")
  await page.getByRole("button", { name: "Send reset link" }).click()
  await expect(page.getByText("j***@one.example")).toBeVisible()
  await expectAppHeader(page)
  expect(
    seen
      .filter((request) => request.path === "/api/v1/auth/forgot-password")
      .map((request) => request.body)
  ).toEqual([{ email: "jane@one.example" }])
})
