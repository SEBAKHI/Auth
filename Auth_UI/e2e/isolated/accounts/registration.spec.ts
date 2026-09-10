import { expect, test, type Page } from "@playwright/test"

import {
  fulfillJson,
  installAnonymousApi,
  loginResponse,
  type SeenRequest,
} from "./mock-anonymous-api"

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
  refuseCompletionWith?: { status: number; title: string; detail: string }
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
          await fulfillJson(
            route,
            options.refuseCompletionWith,
            options.refuseCompletionWith.status
          )
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

test("email, then the code, then the name and password - reloading each of the last two screens", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSignUpApi(page, seen)

  // The first screen asks for the address and nothing else, and its sign-in
  // link keeps the pending authorize request.
  await page.goto(`/register${RETURN_TO}`)
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
  // origin, which this suite does not serve; the departure is the assertion.
  await page.waitForURL((url) => !url.pathname.startsWith("/register"))
  expect(page.url()).toBe(AUTHORIZE)

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
})

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
    refuseCompletionWith: {
      status: 400,
      title: "EmailVerification.InvalidOrExpiredOtp",
      detail: "The code is wrong or has expired.",
    },
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
