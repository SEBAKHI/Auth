import { expect, request, test, type APIRequestContext, type Page } from "@playwright/test"
import { execSync } from "node:child_process"
import * as fs from "node:fs"
import * as path from "node:path"
import { fileURLToPath } from "node:url"

/**
 * Account-deletion E2E journeys 1–3 (plan §11) against the local dev stack.
 *
 * Prerequisites (same as `pnpm gen:api`):
 *  - Auth API running via the `https` launch profile (https://localhost:5101)
 *    with dev defaults: Email:Enabled=false, Notifications:UseOutbox=false —
 *    OTPs and would-be emails are then written to the Serilog file, which is
 *    the established E2E practice for reading verification codes.
 *  - The Vite dev servers are booted/reused by playwright.config.ts.
 *
 * Journeys 4 (post-grace worker execution) and 5 (stubbed Apple) are covered
 * at the unit/integration level (worker + Apple provider seam tests) and by
 * the §12 staging pass; they need API config overrides no black-box UI test
 * can apply.
 */

const API_BASE = "https://localhost:5101"
const PASSWORD = "E2e!Passw0rd#2026"

/** Dev SQL instance — the same one the local Auth API talks to. */
const SQL_SERVER = "localhost\\SQLEXPRESS01"
const SQL_DB = "Astoom_Auth"

const LOGS_DIR = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../../../Auth/Auth_API/Logs"
)

// Serial (one worker, in order) instead of fullyParallel: OTP log lines carry
// MASKED emails, so concurrent journeys can read each other's codes, and the
// deletion endpoints share the strict "login" rate-limit bucket per IP.
test.describe.configure({ mode: "serial" })

function uniqueEmail(tag: string): string {
  return `e2e-del-${tag}-${Date.now()}@example.com`
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
}

/** Mirrors the API's EmailMasking.Mask — OTP log lines mask the address. */
function maskEmail(email: string): string {
  const at = email.indexOf("@")
  const local = email.slice(0, at)
  const domain = email.slice(at)
  if (local.length <= 2) return `${local[0]}***${domain}`
  return `${local[0]}${"*".repeat(Math.min(local.length - 2, 4))}${local.at(-1)}${domain}`
}

/** Newest Serilog file content (the sink rolls to _00N suffixes on size). */
function readLatestLog(): string {
  const newest = fs
    .readdirSync(LOGS_DIR)
    .filter((f) => f.startsWith("auth-api-") && f.endsWith(".log"))
    .map((f) => path.join(LOGS_DIR, f))
    .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs)[0]
  if (!newest) throw new Error(`No Serilog files found in ${LOGS_DIR}`)
  return fs.readFileSync(newest, "utf8")
}

/** Position marker so a search can be limited to lines logged after "now". */
function logOffset(): number {
  return readLatestLog().length
}

/**
 * Polls the Serilog file for `pattern` in content appended after `since`
 * (masked OTP lines are ambiguous across users, so recency matters). Returns
 * the first capture group. Serilog renders string properties quoted, so
 * patterns must tolerate optional quotes around interpolated values.
 */
async function waitForLogMatch(
  pattern: RegExp,
  since = 0,
  timeoutMs = 15_000
): Promise<string> {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const content = readLatestLog()
    const tail = content.slice(Math.min(since, content.length))
    const matches = [...tail.matchAll(new RegExp(pattern, "g"))]
    const last = matches.at(-1)
    if (last) return last[1] ?? last[0]
    if (Date.now() > deadline) {
      throw new Error(`Timed out waiting for log pattern: ${pattern}`)
    }
    await new Promise((r) => setTimeout(r, 500))
  }
}

/** OTP logged by the register/resend verification path (email disabled). */
function registrationOtpPattern(email: string): RegExp {
  return new RegExp(
    `Email disabled - OTP for "?${escapeRegExp(maskEmail(email))}"?: "?(\\d{6})"?`
  )
}

/** OTP logged by the deletion re-auth / public wizard path (email disabled). */
function deletionOtpPattern(email: string): RegExp {
  return new RegExp(
    `Email disabled - deletion OTP for "?${escapeRegExp(maskEmail(email))}"?: "?(\\d{6})"?`
  )
}

/** "Would have sent" line for a template email, matched by EN subject. */
function sentEmailPattern(email: string, subject: string): RegExp {
  return new RegExp(
    `Would have sent to "?${escapeRegExp(email)}"? \\["?en"?\\]: "?${escapeRegExp(subject)}"?`
  )
}

/**
 * POST with one self-healing retry when the strict per-IP rate limit trips —
 * back-to-back suite runs share the same 60-second auth-policy bucket.
 */
async function apiPost(api: APIRequestContext, url: string, data: unknown) {
  let response = await api.post(url, { data })
  if (response.status() === 429) {
    const body = (await response.json().catch(() => null)) as {
      retryAfter?: number
    } | null
    await new Promise((r) => setTimeout(r, ((body?.retryAfter ?? 60) + 1) * 1000))
    response = await api.post(url, { data })
  }
  return response
}

/**
 * Provisions a login-capable throwaway user through the real product flow:
 * register → read the verification OTP from the Serilog file → verify-email
 * (which also consumes the code so it can't shadow later deletion OTPs).
 */
async function createConfirmedUser(api: APIRequestContext, tag: string): Promise<string> {
  const email = uniqueEmail(tag)
  const since = logOffset()

  const registered = await apiPost(api, "/api/v1/Auth/register", {
    email,
    password: PASSWORD,
    firstName: "E2e",
    lastName: `Deletion-${tag}`,
  })
  expect(registered.ok(), `register failed: ${await registered.text()}`).toBe(true)

  const otp = await waitForLogMatch(registrationOtpPattern(email), since)

  const verified = await apiPost(api, "/api/v1/Auth/verify-email", { email, otp })
  expect(verified.ok(), `verify-email failed: ${await verified.text()}`).toBe(true)

  return email
}

/**
 * Turns a freshly registered account into an external-only one (no local
 * password) — the only kind the API lets re-authenticate with an emailed code.
 * E2E cannot mint a real Google/Apple token, so the hash is cleared directly,
 * which is exactly what an external-login-created account looks like.
 */
function clearPasswordHash(email: string): void {
  try {
    // -I: QUOTED_IDENTIFIER ON — required to write through filtered indexes.
    execSync(
      `sqlcmd -S ${SQL_SERVER} -E -d ${SQL_DB} -b -I -Q "UPDATE [dbo].[Users] SET [PasswordHash] = NULL WHERE [Email] = '${email}'"`,
      { stdio: "pipe" }
    )
  } catch (error) {
    const e = error as { status?: number; stderr?: Buffer; stdout?: Buffer }
    throw new Error(
      `sqlcmd failed (${e.status}): ${e.stderr?.toString() ?? ""}${e.stdout?.toString() ?? ""}`
    )
  }
}

/**
 * Registers, strips the password, then confirms the email — the anonymous
 * verify-email path signs the user in, and its refresh token lets the browser
 * adopt the session the same way the SPA does (no login form to type into).
 */
async function createPasswordlessUser(
  api: APIRequestContext,
  tag: string
): Promise<{ email: string; refreshToken: string }> {
  const email = uniqueEmail(tag)
  const since = logOffset()

  const registered = await apiPost(api, "/api/v1/Auth/register", {
    email,
    password: PASSWORD,
    firstName: "E2e",
    lastName: `Deletion-${tag}`,
  })
  expect(registered.ok(), `register failed: ${await registered.text()}`).toBe(true)

  clearPasswordHash(email)

  const otp = await waitForLogMatch(registrationOtpPattern(email), since)
  const verified = await apiPost(api, "/api/v1/Auth/verify-email", { email, otp })
  expect(verified.ok(), `verify-email failed: ${await verified.text()}`).toBe(true)

  const body = (await verified.json()) as { token?: { refreshToken?: string } }
  const refreshToken = body.token?.refreshToken
  expect(refreshToken, "verify-email did not return a session").toBeTruthy()
  return { email, refreshToken: refreshToken as string }
}

async function signIn(page: Page, email: string): Promise<void> {
  await page.goto("/login")
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Password", { exact: true }).fill(PASSWORD)
  await page.getByRole("button", { name: "Sign in" }).click()
}

async function signInExpectingProfile(page: Page, email: string): Promise<void> {
  await signIn(page, email)
  await expect(page).toHaveURL(/\/profile/)
}

/** Opens the danger-zone re-auth dialog from the profile's Account tab. */
async function openDeleteDialog(page: Page): Promise<void> {
  await page.goto("/profile")
  await page.getByRole("button", { name: "Delete account" }).click()
  await expect(page.getByRole("dialog")).toBeVisible()
}

/** Re-auth "Continue" → typed-email confirmation → "Schedule deletion". */
async function confirmScheduleDeletion(page: Page, email: string): Promise<void> {
  await page.getByRole("dialog").getByRole("button", { name: "Continue" }).click()
  const confirm = page.getByRole("alertdialog")
  await expect(confirm).toBeVisible()

  // The destructive button stays disabled until the typed email matches.
  const schedule = confirm.getByRole("button", { name: "Schedule deletion" })
  await expect(schedule).toBeDisabled()
  await confirm.getByRole("textbox").fill(email)
  await schedule.click()

  await expect(page).toHaveURL(/\/deletion-scheduled/)
  await expect(page.getByRole("heading", { name: "Account deactivated" })).toBeVisible()
  await expect(page.getByText("days to recover")).toBeVisible()
}

/** Login with valid credentials must detour to recovery; restoring signs in. */
async function recoverViaLogin(page: Page, email: string): Promise<void> {
  await signIn(page, email)
  await expect(page).toHaveURL(/\/account-recovery/)
  await expect(page.getByRole("alert")).toBeVisible()

  // Email arrives prefilled from the login attempt; only the password is asked.
  await expect(page.getByLabel("Email")).toHaveValue(email)
  await page.getByLabel("Password", { exact: true }).fill(PASSWORD)
  await page.getByRole("button", { name: "Restore my account" }).click()
  await expect(page).toHaveURL(/\/profile/)
}

test.describe("account deletion", () => {
  let api: APIRequestContext

  test.beforeAll(async () => {
    api = await request.newContext({
      baseURL: API_BASE,
      ignoreHTTPSErrors: true,
    })
  })

  test.afterAll(async () => {
    await api.dispose()
  })

  test("journey 1: in-app request with password, recovery via login, cancellation email", async ({
    page,
  }) => {
    test.setTimeout(120_000)
    const email = await createConfirmedUser(api, "j1")

    await signInExpectingProfile(page, email)
    await openDeleteDialog(page)
    await page.getByLabel("Current password").fill(PASSWORD)
    await confirmScheduleDeletion(page, email)

    // Acknowledgment email (R6) went out at request time.
    await waitForLogMatch(
      sentEmailPattern(email, "Your Account Deletion Has Been Scheduled")
    )

    // The 202 revoked every credential: the session is gone, and a fresh
    // login with VALID credentials lands on recovery instead of the app.
    await recoverViaLogin(page, email)

    // Cancellation is confirmed by email + the account is fully usable again.
    await waitForLogMatch(sentEmailPattern(email, "Your Account Has Been Restored"))
  })

  test("journey 2: emailed-code re-auth for a passwordless (external-only) account", async ({
    page,
  }) => {
    test.setTimeout(120_000)
    const { email, refreshToken } = await createPasswordlessUser(api, "j2")

    // No password ⇒ no login form; adopt the session the way the SPA does —
    // refresh token in localStorage, silent refresh on first load.
    await page.addInitScript(
      (token) => localStorage.setItem("auth.refreshToken", token),
      refreshToken
    )
    await openDeleteDialog(page)

    // The dialog offers ONLY the emailed-code factor for passwordless
    // accounts — the server rejects the weaker factor for password holders.
    await expect(page.getByLabel("Current password")).toHaveCount(0)
    const since = logOffset()
    await page.getByRole("button", { name: "Email me a code" }).click()
    await expect(page.getByText("Verification code sent.")).toBeVisible()
    const otp = await waitForLogMatch(deletionOtpPattern(email), since)
    await page.getByRole("textbox", { name: "Verification code" }).fill(otp)

    await confirmScheduleDeletion(page, email)

    // Grace recovery for external-only accounts needs the live provider
    // credential (covered by the RecoverAccountExternal handler tests), so
    // the journey ends at the scheduled screen.
  })

  test("journey 3: public wizard, generic responses, pending state via login", async ({
    page,
  }) => {
    test.setTimeout(120_000)
    const email = await createConfirmedUser(api, "j3")

    // The discoverability chain (plan §9 entry-point decision): the sign-in
    // screen links the privacy policy — never the wizard directly — and the
    // policy's deletion section carries the "Delete my account" entry point
    // required by store data-deletion policies.
    await page.goto("/login")
    await expect(page.getByRole("link", { name: /delete/i })).toHaveCount(0)
    await page.getByRole("link", { name: "Privacy policy" }).click()
    await expect(page).toHaveURL(/\/privacy/)
    await expect(
      page.getByRole("heading", { name: "Privacy Policy" })
    ).toBeVisible()

    await page.getByRole("button", { name: "Delete my account" }).click()
    await expect(page).toHaveURL(/\/delete-account/)
    await expect(
      page.getByRole("heading", { name: "Delete your account" })
    ).toBeVisible()

    const since = logOffset()
    await page.getByLabel("Email").fill(email)
    await page.getByRole("button", { name: "Send verification code" }).click()
    await expect(page.getByText(`Enter the 6-digit code we sent to ${email}`)).toBeVisible()

    const otp = await waitForLogMatch(deletionOtpPattern(email), since)
    await page.getByRole("textbox", { name: "Verification code" }).fill(otp)
    await page.getByRole("button", { name: "Confirm deletion" }).click()
    await expect(page.getByText("Request received")).toBeVisible()

    // Anti-enumeration: a nonexistent address gets the exact same generic
    // advance to the code step — the page never reveals account existence.
    const ghost = uniqueEmail("ghost")
    await page.goto("/delete-account")
    await page.getByLabel("Email").fill(ghost)
    await page.getByRole("button", { name: "Send verification code" }).click()
    await expect(page.getByText(`Enter the 6-digit code we sent to ${ghost}`)).toBeVisible()

    // The real account is genuinely pending: valid login detours to recovery.
    // Restoring closes the loop (public-initiated deletion is grace-recoverable).
    await recoverViaLogin(page, email)
  })
})
