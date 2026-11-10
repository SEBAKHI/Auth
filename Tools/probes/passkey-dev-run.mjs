/**
 * PasskeyDevRun (S20 PR C): passkeys end to end on a developer machine, against
 * the worktree's API and both apps, with a CDP virtual authenticator standing in
 * for the device. The xUnit suite pins the server's rules and the browser harness
 * the pages under the real CSP; this shows the real API, the real database and
 * the real WebAuthn library agreeing on a real ceremony.
 *
 * PRECONDITION, NOT OPTIONAL: run it only on a throwaway LocalDB that the owner
 * allowed for this run (owner decision D-S20-8), never on a shared database. The
 * probe refuses any non-localhost URL and any server that is not LocalDB.
 *
 * The stack it expects (each in its own throwaway configuration):
 *   - the worktree's DACPAC published to the throwaway LocalDB, and the seeded
 *     administrator given a password (Auth_Setup --email);
 *   - the worktree's API, in its own environment only (never a settings file), with:
 *       Passkeys__Enabled=true, Passkeys__RpId=localhost,
 *       IdentityProvider__ApplicationMfaEnforcement=Enforce,
 *       Email__Enabled=true, Email__SmtpHost=127.0.0.1, Email__SmtpPort=1, Email__UseSsl=false
 *         (every delivery fails on the closed port, so each rendered code stays in
 *         NotificationOutbox, where the probe reads it),
 *       Password__BreachedPasswordCheck__Enabled=false, RateLimiting__LoginPermitLimit=1000,
 *       RateLimiting__SignInPagePermitLimit=1000, Email__MaxOtpRequestsPerWindow=50,
 *       and the probe page's origin appended to Cors:AllowedOrigins by index
 *       (Cors__AllowedOrigins__<n>, never replacing the list: an array binds by index),
 *       not to IdentityProvider:FirstPartySpaOrigins;
 *       and the refresh cookie on: IdentityProvider__SpaRefreshCookieEnabled=true, with both
 *       apps' origins in IdentityProvider__FirstPartySpaOrigins__0/1 (the window's refresh
 *       after a proof rides the browser's own cookie);
 *   - both apps on https dev servers, with a certificate given through DEV_HTTPS_CERT
 *     and DEV_HTTPS_KEY (never read from .env.*.local).
 *
 * Environment:
 *   PROBE_CONSOLE, PROBE_ACCOUNTS   https://localhost:<port> of each app
 *   PROBE_API                       the API's base URL (https://localhost:<port>)
 *   PROBE_SQL_SERVER, PROBE_DB      the throwaway LocalDB instance and database
 *   PROBE_ADMIN_EMAIL, PROBE_ADMIN_PASSWORD
 *                                   a full platform administrator with no second factor,
 *                                   who creates the accounts and the application
 *   PROBE_OTHER_PORT                the port of the probe's own page (origin scenario)
 *   PROBE_APP_PORT                  the port of the probe's own application (app-return)
 *   DEV_HTTPS_CERT, DEV_HTTPS_KEY   the certificate, for those two
 *
 * Scenarios (positional argument). Each makes its own fresh accounts, so a rerun on
 * the same database needs nothing reset:
 *   add-and-sign-in     Signs in to the console (password, then the app's code), adds
 *                       a passkey in Profile › Security, renames it, reads its row
 *                       (RP ID localhost, user-verified, live), the passkey.registered
 *                       audit row and the outbox rows written for the account; then
 *                       signs in at the accounts app with the passkey button and reads
 *                       the row's LastUsedAt and that sign-in's own user.login audit row.
 *   origin              From a page on PROBE_OTHER_PORT (in CORS, not a first-party
 *                       origin) asks for passkey sign-in options: refused 403
 *                       Auth.FirstPartyOriginRequired.
 *   app-return          The owner's condition: a registered public client with a
 *                       localhost callback served here. From a real authorize request,
 *                       signs in at the accounts app with the suggestion, then with the
 *                       button, each ending on the callback with a code the token
 *                       endpoint redeems. Then an application that requires two steps,
 *                       enforcement on, for an account whose only factor is a passkey:
 *                       the passkey at once reaches the callback; the password leads to
 *                       mfa_passkey, and the passkey from there to the callback; and the
 *                       "I lost my passkey" link leads to Profile › Security.
 *   locks               An administrator's lock refuses a passkey older than 72 hours
 *                       (a younger one it revokes) with its own code; a timed lock from
 *                       wrong passwords admits it and moves no count.
 *   passkey-only-admin  A passkey-only administrator's password shows the API's sentence
 *                       and the passkey first; then the passkey signs in, with the
 *                       platform authority; then, on a session past the window, the window
 *                       asks for that passkey alone, and the addition goes on from it.
 *   recovery            An account in its deletion grace period: the passkey sign-in goes
 *                       to the recovery page, whose passkey choice cancels the deletion.
 *   proof-password      A session past the window adds a passkey: the window opens where
 *                       the person stands. A wrong password is refused there, counted as at
 *                       the sign-in page and stamps nothing; the right one closes it, and
 *                       the addition goes on by itself: same document, same session, the
 *                       session stamped with a password, the count kept, the audit row
 *                       written with the address and the browser. The proof survives a
 *                       reload. Then the passkey is added.
 *   proof-passkey       A password session of a passkey-only account adds a passkey (T-C7):
 *                       the window asks for the passkey alone, it proves the person, and the
 *                       addition goes on; the device is then asked. Then the removal.
 *   proof-app           An account with an authenticator app, past the window: a wrong code
 *                       is refused, stamps nothing, and leaves a failure row of the proof's
 *                       action (the password was right); the password and a code that nothing
 *                       used go on. Past the window again, the passkey alone goes on.
 *   proof-disable       Switching the app off, past the window. A proof with the app's code
 *                       spends that time step, so the code the page held is not sent again:
 *                       no failure is counted, the field is empty and the person is asked for
 *                       the next code, which goes on. A proof with the passkey leaves the held
 *                       code good, and the switch-off goes on by itself.
 *   proof-new-codes     New recovery codes, past the window: the dialog stays under the window,
 *                       and after a proof with the app's code its field is empty, with the
 *                       next-code sentence; the next code makes new codes.
 *   proof-removal       Removing a passkey, past the window: the confirmation, the window with
 *                       the passkey and the password, and the removal goes on by itself with the
 *                       passkey that was confirmed; the page still answers a click after it.
 *   proof-not-the-union A session that proved two factors an hour ago and a password now
 *                       has proved a password now: with a token refreshed after that proof, the
 *                       change that needs two still refuses, and one that needs one goes on.
 *   proof-token-copy    A proof counts only for the tokens issued after it: a second browser
 *                       context holding a copy of the session's access token, taken before the
 *                       owner's proof, is refused a sensitive change after it, while the owner's
 *                       own change goes on with the token the window refreshed.
 *   proof-other-account A passkey of another account, on the same device, proves nothing,
 *                       with its own code, stamps nothing, and records no use of that passkey.
 *   proof-email         An account with no password: the window sends a code by email, under
 *                       the seeded template's own wording; a wrong one is refused, the right
 *                       one goes on, and the addition still asks for its own emailed code.
 *   proof-way-out       The window's way out asks first, ends the session, and the sign-in
 *                       comes back to the page.
 *   proof-unrecorded    With enforcement on, an administrator's session whose sign-in is not
 *                       recorded lands on the required page; the window proves the person and
 *                       they go where they were going, in the same session. The switch is
 *                       set and reset through the settings API, so this overrides nothing
 *                       else in the section: run it on a database with no TwoFactor overrides.
 *   before-the-columns  What an API deployed before the DACPAC does: it drops the two columns
 *                       (this database is a throwaway), shows /ready says Degraded so a deploy
 *                       stops, a sign-in and a change on a recent session still work and a stale
 *                       session fails closed, then puts them back and /ready recovers. Last in
 *                       the order, for that reason.
 *   all                 All of them, in order.
 *
 * Verdicts: PASS, FAIL, INCONCLUSIVE (a precondition the probe cannot meet).
 * Nothing it writes outlives the throwaway database: drop it at the end.
 */
import { createHash, createHmac, randomBytes } from "node:crypto"
import { execFileSync } from "node:child_process"
import fs from "node:fs"
import https from "node:https"
import { createRequire } from "node:module"
import os from "node:os"
import path from "node:path"

// The one Playwright of this repository, the UI workspace's: Tools/ has no node_modules.
const { chromium } = createRequire(new URL("../../Auth_UI/package.json", import.meta.url))("@playwright/test")

class Inconclusive extends Error {}

const env = (name) => {
  const value = process.env[name]
  if (!value) throw new Inconclusive(`${name} is not set`)
  return value
}

function localOnly(url) {
  const { hostname, protocol } = new URL(url)
  if (hostname !== "localhost" || protocol !== "https:") {
    throw new Inconclusive(`${url} is not an https://localhost URL`)
  }
  return url.replace(/\/$/, "")
}

// Every URL this probe calls has just passed localOnly: the certificate is a local one.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0"

const consoleUrl = () => localOnly(env("PROBE_CONSOLE"))
const accountsUrl = () => localOnly(env("PROBE_ACCOUNTS"))
const apiUrl = () => localOnly(env("PROBE_API"))

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

/** RFC 6238: the authenticator app's code for the given step. */
function totp(secretBase32, step = Math.floor(Date.now() / 30_000)) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const char of secretBase32.replace(/[\s=]/g, "").toUpperCase()) {
    const index = alphabet.indexOf(char)
    if (index < 0) throw new Inconclusive("the app's secret is not base32")
    bits += index.toString(2).padStart(5, "0")
  }
  const key = Buffer.from(bits.match(/.{8}/g).map((byte) => parseInt(byte, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(step))
  const digest = createHmac("sha1", key).update(counter).digest()
  const offset = digest[digest.length - 1] & 0x0f
  const code = (digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000
  return String(code).padStart(6, "0")
}

/** Runs a statement through a file: no quoting on the command line, and a path sqlcmd reads. */
function sql(query) {
  const server = env("PROBE_SQL_SERVER")
  if (!/^\(localdb\)\\/i.test(server)) throw new Inconclusive(`${server} is not a LocalDB instance`)
  const file = path.join(os.tmpdir(), `passkey-dev-run-${process.pid}-${randomBytes(4).toString("hex")}.sql`)
  fs.writeFileSync(file, `SET NOCOUNT ON;\n${query}`)
  try {
    return execFileSync("sqlcmd", ["-S", server, "-d", env("PROBE_DB"), "-E", "-I", "-b", "-h", "-1", "-W", "-s", "|", "-i", file], {
      encoding: "utf8",
    })
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
  } finally {
    fs.unlinkSync(file)
  }
}

/** The database's own clock: every time the API stores is its own UTC, read the same way. */
const sqlNow = () => sql("SELECT CONVERT(NVARCHAR(30), SYSUTCDATETIME(), 126);")[0]

const results = []

function verdict(name, outcome, detail) {
  results.push({ name, outcome, detail })
  console.log(`${outcome.padEnd(12)} ${name}${detail ? ` - ${detail}` : ""}`)
}

const check = (name, ok, detail) => verdict(name, ok ? "PASS" : "FAIL", detail)

/** A call to the API as the app would make it: from a first-party origin. */
async function call(method, route, { token, body, form, headers = {}, origin = accountsUrl(), cookie } = {}) {
  const sent = { Origin: origin, ...headers }
  if (token) sent.Authorization = `Bearer ${token}`
  if (cookie) sent.Cookie = cookie
  let payload
  if (form) {
    sent["Content-Type"] = "application/x-www-form-urlencoded"
    payload = new URLSearchParams(form).toString()
  } else if (body !== undefined) {
    sent["Content-Type"] = "application/json"
    payload = JSON.stringify(body)
  }
  const response = await fetch(`${apiUrl()}${route}`, { method, headers: sent, body: payload })
  const text = await response.text()
  let json
  try {
    json = text ? JSON.parse(text) : undefined
  } catch {
    json = undefined
  }
  return { status: response.status, json, text, headers: response.headers }
}

async function administratorToken() {
  const signedIn = await call("POST", "/api/v1/auth/login", {
    body: { email: env("PROBE_ADMIN_EMAIL"), password: env("PROBE_ADMIN_PASSWORD") },
  })
  const token = signedIn.json?.token?.accessToken
  if (!token) throw new Inconclusive(`the administrator cannot sign in: ${signedIn.status} ${signedIn.text.slice(0, 200)}`)
  return token
}

const SUPER_ADMIN_ROLE = "10000000-0000-0000-0000-000000000001"

/** A new account with a confirmed address, made by the administrator. */
async function freshAccount(label, { administrator = false } = {}) {
  const email = `devrun-${label}-${Date.now().toString(36)}@example.com`
  const password = `Pk-${randomBytes(12).toString("base64url")}!7a`
  const token = await administratorToken()
  const created = await call("POST", "/api/v1/users", {
    token,
    body: { email, password, firstName: "Dev", lastName: label },
  })
  if (created.status !== 201) throw new Inconclusive(`cannot create ${email}: ${created.status} ${created.text.slice(0, 200)}`)
  const id = created.json.id
  sql(`UPDATE dbo.Users SET IsEmailConfirmed = 1, MustChangePassword = 0 WHERE Id = '${id}';`)
  if (administrator) {
    const assigned = await call("POST", `/api/v1/users/${id}/roles`, { token, body: { roleId: SUPER_ADMIN_ROLE } })
    if (assigned.status >= 300) throw new Inconclusive(`cannot make ${email} an administrator: ${assigned.status}`)
  }
  return { email, password, id }
}

/** The six digits in the newest outbox row of the type: every delivery fails, so the body is kept. */
async function emailedCode(email, since, typeCode) {
  for (let attempt = 0; attempt < 40; attempt++) {
    const rows = sql(`SELECT TOP 1 SUBSTRING(BodyHtml, PATINDEX('%otp-code' + CHAR(34) + '>%', BodyHtml) + 10, 6)
      FROM dbo.NotificationOutbox
      WHERE Recipient = N'${email}' AND NotificationTypeCode = N'${typeCode}' AND BodyHtml LIKE '%otp-code%' AND CreatedAt >= '${since}'
      ORDER BY CreatedAt DESC;`)
    if (rows[0] && /^\d{6}$/.test(rows[0])) return rows[0]
    await sleep(500)
  }
  throw new Inconclusive(`no ${typeCode} code for ${email} in the outbox: is the API's Email:SmtpPort the closed port?`)
}

/** Binds the authenticator app through the API, and returns its secret. */
async function bindApp(account) {
  const signedIn = await call("POST", "/api/v1/auth/login", { body: { email: account.email, password: account.password } })
  const token = signedIn.json?.token?.accessToken
  if (!token) throw new Inconclusive(`${account.email} cannot sign in: ${signedIn.status} ${signedIn.text.slice(0, 200)}`)
  const setup = await call("POST", "/api/v1/auth/2fa/setup", { token })
  if (setup.status !== 200) throw new Inconclusive(`2fa/setup: ${setup.status} ${setup.text.slice(0, 200)}`)
  let emailCode
  if (setup.json.emailCodeRequired) {
    const since = sqlNow()
    await call("POST", "/api/v1/auth/2fa/email-code", { token, body: {} })
    emailCode = await emailedCode(account.email, since, "two-factor-bind-code")
  }
  const enabled = await call("POST", "/api/v1/auth/2fa/enable", {
    token,
    body: { code: totp(setup.json.secret), emailCode },
  })
  if (enabled.status !== 200) throw new Inconclusive(`2fa/enable: ${enabled.status} ${enabled.text.slice(0, 200)}`)
  return setup.json.secret
}

/** A page whose own authenticator stands in for the device. `clear` forgets every session. */
async function withAuthenticator(run) {
  const browser = await chromium.launch()
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } })
  const page = await context.newPage()
  const cdp = await context.newCDPSession(page)
  await cdp.send("WebAuthn.enable")
  await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  })
  const clear = async () => {
    await context.clearCookies()
    for (const origin of [consoleUrl(), accountsUrl()]) {
      await cdp.send("Storage.clearDataForOrigin", { origin, storageTypes: "local_storage,session_storage" })
    }
  }
  try {
    return await run(page, clear)
  } finally {
    await browser.close()
  }
}

/**
 * This page's browser offers no passkey suggestion. With a virtual authenticator that
 * simulates the person's presence, the address field's request completes by itself, so
 * the page signs in before anything can be clicked. From here on, in this page.
 */
async function withoutSuggestion(page) {
  await page.addInitScript(() => {
    Object.defineProperty(PublicKeyCredential, "isConditionalMediationAvailable", {
      configurable: true,
      value: async () => false,
    })
  })
}

const left = (from) => (url) => !url.pathname.startsWith(from)

async function typePassword(page, { email, password }) {
  await page.getByLabel("Email").fill(email)
  const field = page.locator('input[type="password"]')
  if (!(await field.isVisible())) await page.getByRole("button", { name: "Sign in with a password instead" }).click()
  await field.fill(password)
  await page.getByRole("button", { name: "Sign in", exact: true }).click()
}

async function signInToConsole(page, account, appSecret) {
  await page.goto(`${consoleUrl()}/login`, { waitUntil: "networkidle" })
  await typePassword(page, account)
  if (appSecret) {
    // Six digits submit by themselves. A code offered twice is refused as replayed, so
    // this waits for a step binding the app did not use.
    const bound = totp(appSecret)
    for (let waited = 0; waited < 35 && totp(appSecret) === bound; waited++) await sleep(1000)
    await page.getByLabel("Verification code", { exact: true }).fill(totp(appSecret))
  }
  await page.waitForURL((url) => !/\/(login|two-factor)/.test(url.pathname), { timeout: 30_000 })
}

/** Profile › Security, with its passkeys card ready. */
async function openSecurity(page) {
  await page.goto(`${consoleUrl()}/profile?tab=security`)
  const card = page.getByTestId("passkeys-card")
  await card.getByRole("button", { name: "Add a passkey" }).waitFor({ timeout: 30_000 })
  return card
}

const addDialog = (page) => page.getByRole("dialog", { name: "Add a passkey" })

/** Finishes the addition from its open dialog. */
async function createInDialog(card, dialog, account, name) {
  await dialog.getByLabel("Name").fill(name)
  const send = dialog.getByRole("button", { name: "Email me a code" })
  if (await send.isVisible()) {
    const since = sqlNow()
    await send.click()
    const code = await emailedCode(account.email, since, "two-factor-bind-code")
    await dialog.getByLabel("Code from the email", { exact: true }).fill(code)
  }
  await dialog.getByRole("button", { name: "Create the passkey" }).click()
  await card.getByText(name).waitFor({ timeout: 30_000 })
}

async function addPasskey(page, account, name) {
  const card = await openSecurity(page)
  await card.getByRole("button", { name: "Add a passkey" }).click()
  const dialog = addDialog(page)
  await dialog.waitFor()
  await createInDialog(card, dialog, account, name)
  return card
}

const passkeyButton = (page, name = "Sign in with a passkey") =>
  page.getByTestId("passkey-sign-in").getByRole("button", { name })

async function pressPasskey(page, name) {
  const button = passkeyButton(page, name)
  await button.waitFor({ timeout: 30_000 })
  await button.click()
}

function rowOf(id) {
  const [status, count, end] = sql(`SELECT CONCAT(Status, '|', FailedLoginAttempts, '|',
    CASE WHEN LockoutEndUtc IS NULL THEN 'none' WHEN LockoutEndUtc > SYSUTCDATETIME() THEN 'future' ELSE 'past' END)
    FROM dbo.Users WHERE Id = '${id}';`)[0].split("|")
  return { status: Number(status), count: Number(count), end }
}

// ── The window that proves a person in place ───────────────────────────────

/** LastProofMethods reads these fixed bits (AuthenticationMethods): the column's own numbers. */
const STORED = { password: 1, passwordAndApp: 9, passkey: 96, emailCodeFirstFactor: 132 }

/** The account's newest live session: the one a sign-in just opened. */
function newestSession(userId) {
  const rows = sql(`SELECT TOP 1 Id FROM dbo.UserSessions WHERE UserId = '${userId}' AND EndedAt IS NULL ORDER BY StartedAt DESC;`)
  if (!rows[0]) throw new Inconclusive(`no live session for ${userId}`)
  return rows[0]
}

const liveSessions = (userId) =>
  Number(sql(`SELECT COUNT(*) FROM dbo.UserSessions WHERE UserId = '${userId}' AND EndedAt IS NULL;`)[0])

/** What the row says of a session: is it live, what its last proof proved, what it ever proved. */
function sessionRow(sessionId) {
  const [live, proof, methods] = sql(`SELECT CONCAT(CASE WHEN EndedAt IS NULL THEN 'live' ELSE 'ended' END, '|',
    ISNULL(CONVERT(NVARCHAR(12), LastProofMethods), 'none'), '|', ISNULL(CONVERT(NVARCHAR(12), AuthMethods), 'none'))
    FROM dbo.UserSessions WHERE Id = '${sessionId}';`)[0].split("|")
  return { live: live === "live", proof, methods }
}

/** Past any window an operator can set (60 minutes at most), the sign-in and the last proof both. */
function ageSession(sessionId) {
  sql(`UPDATE dbo.UserSessions SET StartedAt = DATEADD(MINUTE, -100, SYSUTCDATETIME()),
    LastProofAt = CASE WHEN LastProofAt IS NULL THEN NULL ELSE DATEADD(MINUTE, -100, SYSUTCDATETIME()) END
    WHERE Id = '${sessionId}';`)
}

/** Marks this document: nothing but a reload or a navigation away can take the mark off. */
const markDocument = (page) =>
  page.evaluate(() => {
    window.__devRunDocument = true
  })
const sameDocument = (page) => page.evaluate(() => window.__devRunDocument === true)

const proofWindow = (page) => page.getByRole("dialog", { name: "Confirm it's you" })
const confirmProof = (proof) => proof.getByRole("button", { name: "Confirm", exact: true }).click()
const passkeyInWindow = (proof) => proof.getByRole("button", { name: "Confirm with my passkey" })

/** Waits for a code that nothing used yet: a code offered twice is refused as replayed. */
async function unusedCode(secret, used) {
  for (let waited = 0; waited < 35 && totp(secret) === used; waited++) await sleep(1000)
  return totp(secret)
}

/** Another code, never the current one: the next number along. */
const notTheCode = (code) => String((Number(code) + 1) % 1_000_000).padStart(6, "0")

async function auditCount(userId, since) {
  let count = 0
  for (let attempt = 0; attempt < 20 && count === 0; attempt++) {
    count = Number(sql(`SELECT COUNT(*) FROM dbo.AuditLogs WHERE UserId = '${userId}' AND Action = N'session.reauthenticated' AND IsSuccess = 1 AND [Timestamp] >= '${since}';`)[0])
    if (count === 0) await sleep(500)
  }
  return count
}

/**
 * A sign-in through the API as the accounts app makes it, and a refresh of its session: with the
 * refresh cookie on, the refresh token rides the cookie the sign-in set, and the body names none.
 */
async function apiSignIn(account) {
  const signedIn = await call("POST", "/api/v1/auth/login", { body: { email: account.email, password: account.password } })
  const token = signedIn.json?.token?.accessToken
  if (!token) throw new Inconclusive(`${account.email} cannot sign in: ${signedIn.status} ${signedIn.text.slice(0, 200)}`)
  const cookie = signedIn.headers.getSetCookie().map((line) => line.split(";")[0]).join("; ")
  const refreshToken = signedIn.json?.token?.refreshToken
  return {
    token,
    async refresh() {
      const legacy = refreshToken && refreshToken !== "__cookie__"
      const refreshed = await call("POST", "/api/v1/auth/refresh", { cookie, body: legacy ? { refreshToken } : {} })
      const fresh = refreshed.json?.accessToken
      if (!fresh) throw new Inconclusive(`the refresh failed: ${refreshed.status} ${refreshed.text.slice(0, 200)}`)
      return fresh
    },
  }
}

/** The audit rows of the proof's action since `since`, as success|error|address|browser lines. */
const proofRows = (userId, since) =>
  sql(`SELECT CONCAT(IsSuccess, '|', ISNULL(ErrorMessage, ''), '|', ISNULL(IpAddress, ''), '|', CASE WHEN UserAgent IS NULL THEN '' ELSE 'agent' END)
    FROM dbo.AuditLogs WHERE UserId = '${userId}' AND Action = N'session.reauthenticated' AND [Timestamp] >= '${since}' ORDER BY [Timestamp];`)

async function apiSession(account) {
  const signedIn = await call("POST", "/api/v1/auth/login", { body: { email: account.email, password: account.password } })
  const token = signedIn.json?.token?.accessToken
  if (!token) throw new Inconclusive(`${account.email} cannot sign in: ${signedIn.status} ${signedIn.text.slice(0, 200)}`)
  return token
}

// ── add-and-sign-in ─────────────────────────────────────────────────────────

async function addAndSignIn() {
  const account = await freshAccount("add")
  const secret = await bindApp(account)
  const startedAt = sqlNow()

  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account, secret)
    const card = await addPasskey(page, account, "DevRun")

    const row = sql(
      `SELECT CONCAT(RpId, '|', IsUserVerified, '|', CASE WHEN RevokedAt IS NULL THEN 'live' ELSE 'revoked' END) FROM dbo.UserPasskeys WHERE UserId = '${account.id}' AND Name = N'DevRun'`
    )
    check("registered row", row.length === 1 && row[0] === "localhost|1|live", JSON.stringify(row))
    const audit = sql(`SELECT COUNT(*) FROM dbo.AuditLogs WHERE UserId = '${account.id}' AND Action = N'passkey.registered' AND [Timestamp] >= '${startedAt}'`)
    check("audit passkey.registered", audit[0] === "1", JSON.stringify(audit))
    const outbox = sql(`SELECT NotificationTypeCode FROM dbo.NotificationOutbox WHERE Recipient = N'${account.email}' AND CreatedAt >= '${startedAt}'`)
    check("outbox rows since the start", outbox.length > 0, outbox.join(", ") || "none")

    // The API's only PATCH, through the real client.
    await card.getByRole("button", { name: "Rename DevRun" }).click()
    const renaming = card.getByLabel("Name")
    await renaming.fill("DevRun renamed")
    await card.getByRole("button", { name: "Save" }).click()
    await card.getByText("DevRun renamed").waitFor({ timeout: 30_000 })
    const renamed = sql(`SELECT COUNT(*) FROM dbo.UserPasskeys WHERE UserId = '${account.id}' AND Name = N'DevRun renamed'`)
    check("rename (PATCH)", renamed[0] === "1", JSON.stringify(renamed))

    // The accounts app, in the same page: the authenticator is that page's.
    await clear()
    await withoutSuggestion(page)
    await page.goto(`${accountsUrl()}/login`)
    // Taken here, after the password sign-ins above: this sign-in's own row, or nothing.
    const beforeAccounts = sqlNow()
    await pressPasskey(page)
    await page.waitForURL(left("/login"), { timeout: 30_000 })
    const used = sql(`SELECT CASE WHEN LastUsedAt >= '${beforeAccounts}' THEN 'used' ELSE 'unused' END FROM dbo.UserPasskeys WHERE UserId = '${account.id}' AND Name = N'DevRun renamed'`)
    check("passkey sign-in at accounts", used[0] === "used", JSON.stringify(used))
    let login = []
    for (let attempt = 0; attempt < 20 && login.length === 0; attempt++) {
      login = sql(`SELECT TOP 1 Details FROM dbo.AuditLogs WHERE UserId = '${account.id}' AND Action = N'user.login' AND [Timestamp] >= '${beforeAccounts}' ORDER BY [Timestamp] DESC`)
      if (login.length === 0) await sleep(500)
    }
    check("audit user.login of that sign-in", login.length === 1, login[0] ?? "none")
  })
}

// ── origin ──────────────────────────────────────────────────────────────────

function ownServer(port, respond) {
  const server = https.createServer(
    { cert: fs.readFileSync(env("DEV_HTTPS_CERT")), key: fs.readFileSync(env("DEV_HTTPS_KEY")) },
    respond
  )
  return new Promise((resolve) => server.listen(port, "localhost", () => resolve(server)))
}

async function origin() {
  const api = apiUrl()
  const port = Number(env("PROBE_OTHER_PORT"))
  const server = await ownServer(port, (_, response) => {
    response.writeHead(200, { "content-type": "text/html; charset=utf-8" })
    response.end("<!doctype html><title>not first-party</title>")
  })
  try {
    await withAuthenticator(async (page) => {
      await page.goto(`https://localhost:${port}/`)
      const answer = await page.evaluate(async (base) => {
        const response = await fetch(`${base}/api/v1/auth/passkeys/assertion/options`, {
          method: "POST",
          credentials: "include",
        })
        const body = await response.json().catch(() => ({}))
        return { status: response.status, code: body.code }
      }, api)
      check("another origin is refused", answer.status === 403 && answer.code === "Auth.FirstPartyOriginRequired", JSON.stringify(answer))
    })
  } finally {
    server.close()
  }
}

// ── app-return ──────────────────────────────────────────────────────────────

function pkce() {
  const verifier = randomBytes(32).toString("base64url")
  return { verifier, challenge: createHash("sha256").update(verifier).digest("base64url") }
}

/** A registered public client whose callback is this probe's own page. */
async function freshApplication(callback, requireTwoFactor) {
  const code = `DEVRUN-${Date.now().toString(36).toUpperCase()}`
  const created = await call("POST", "/api/v1/Applications", {
    token: await administratorToken(),
    body: {
      code,
      name: `Dev run ${code}`,
      baseUrl: new URL(callback).origin,
      redirectUris: [callback],
      accessMode: "Everyone",
      requireTwoFactor,
    },
  })
  if (created.status >= 300) throw new Inconclusive(`cannot create the application: ${created.status} ${created.text.slice(0, 300)}`)
  return code
}

/** Starts from a real authorize request; `finish` then ends it, or not, at the callback. */
function authorizeRequest(clientId, callback) {
  const { verifier, challenge } = pkce()
  const query = new URLSearchParams({
    response_type: "code",
    client_id: clientId,
    redirect_uri: callback,
    code_challenge: challenge,
    code_challenge_method: "S256",
    state: randomBytes(6).toString("hex"),
    scope: "openid",
  })
  return { url: `${apiUrl()}/api/v1/auth/authorize?${query}`, verifier }
}

async function landedOnCallback(page, name, clientId, callback, verifier) {
  await page.waitForURL((url) => url.href.startsWith(callback), { timeout: 30_000 })
  const params = new URL(page.url()).searchParams
  const code = params.get("code")
  if (!code) {
    check(name, false, `the callback got no code: ${page.url()}`)
    return
  }
  const exchanged = await call("POST", "/api/v1/auth/token", {
    form: { grant_type: "authorization_code", code, redirect_uri: callback, client_id: clientId, code_verifier: verifier },
  })
  check(name, exchanged.status === 200 && Boolean(exchanged.json?.access_token), `token endpoint ${exchanged.status}`)
}

async function appReturn() {
  const port = Number(env("PROBE_APP_PORT"))
  const callback = `https://localhost:${port}/callback`
  const server = await ownServer(port, (_, response) => {
    response.writeHead(200, { "content-type": "text/html; charset=utf-8" })
    response.end("<!doctype html><title>The application</title><h1>The application</h1>")
  })
  try {
    const plain = await freshApplication(callback, false)
    const account = await freshAccount("return")

    await withAuthenticator(async (page, clear) => {
      await signInToConsole(page, account)
      await addPasskey(page, account, "Return")
      await clear()

      // Before withoutSuggestion, which stays for the page's life: on this page the
      // suggestion completes by itself, as a person picking the passkey would.
      const bySuggestion = authorizeRequest(plain, callback)
      await page.goto(bySuggestion.url)
      await landedOnCallback(page, "from an application, the suggestion, back to it", plain, callback, bySuggestion.verifier)
      await clear()

      await withoutSuggestion(page)
      const byButton = authorizeRequest(plain, callback)
      await page.goto(byButton.url)
      await page.waitForURL(`${accountsUrl()}/login?**`, { timeout: 30_000 })
      await pressPasskey(page)
      await landedOnCallback(page, "from an application, the button, back to it", plain, callback, byButton.verifier)
      await clear()

      // The application requires two steps, and this account's only factor is a passkey.
      // The passkey at once: one sign-in that proved both.
      const twoStep = await freshApplication(callback, true)
      const atOnce = authorizeRequest(twoStep, callback)
      await page.goto(atOnce.url)
      await page.waitForURL(`${accountsUrl()}/login?**`, { timeout: 30_000 })
      await pressPasskey(page)
      await landedOnCallback(page, "under two steps, the passkey at once, back to the application", twoStep, callback, atOnce.verifier)
      await clear()

      // The password first: the server sends the person back for the passkey.
      const required = authorizeRequest(twoStep, callback)
      await page.goto(required.url, { waitUntil: "networkidle" })
      await typePassword(page, account)
      await page.waitForURL((url) => url.pathname === "/login" && url.searchParams.get("reason") === "mfa_passkey", { timeout: 30_000 })
      check("the password leads to mfa_passkey", true, new URL(page.url()).search.replace(/auth_request=[^&]+/, "auth_request=…"))
      await pressPasskey(page)
      await landedOnCallback(page, "under two steps (mfa_passkey), the passkey, back to the application", twoStep, callback, required.verifier)
      await clear()

      // The way out that sentence names (D-152-2): no resume, Profile › Security.
      const lost = authorizeRequest(twoStep, callback)
      await page.goto(lost.url, { waitUntil: "networkidle" })
      await typePassword(page, account)
      await page.waitForURL((url) => url.searchParams.get("reason") === "mfa_passkey", { timeout: 30_000 })
      await page.getByTestId("passkey-lost-link").click()
      await page.waitForURL((url) => url.pathname === "/profile", { timeout: 30_000 })
      check("I lost my passkey leads to Profile › Security, not the application", page.url().endsWith("/profile?tab=security"), page.url())
    })
  } finally {
    server.close()
  }
}

// ── locks ───────────────────────────────────────────────────────────────────

async function locks() {
  const account = await freshAccount("locks")
  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Locks")
    await clear()
    await withoutSuggestion(page)

    // An administrator's lock also revokes every passkey registered in the 72 hours before
    // it (D-T19 D), and a revoked passkey is refused for that reason, not for the lock's.
    // So this one is made older than that, and the verdict reads the refusal's own code.
    sql(`UPDATE dbo.UserPasskeys SET CreatedAt = DATEADD(HOUR, -73, SYSUTCDATETIME()) WHERE UserId = '${account.id}';`)
    const token = await administratorToken()
    const locked = await call("POST", `/api/v1/users/${account.id}/lock`, { token, body: { reason: "PasskeyDevRun" } })
    if (locked.status !== 204) throw new Inconclusive(`cannot lock: ${locked.status} ${locked.text.slice(0, 200)}`)
    await page.goto(`${accountsUrl()}/login`)
    const answered = page.waitForResponse((response) => response.url().endsWith("/assertion/verify"), { timeout: 30_000 })
    await pressPasskey(page)
    const refused = await (await answered).json().catch(() => ({}))
    const refusal = page.getByTestId("passkey-message")
    await refusal.waitFor({ timeout: 30_000 })
    check(
      "an administrator's lock refuses the passkey, for the lock",
      /^User\.AccountLocked(Until)?$/.test(refused.code ?? "") && new URL(page.url()).pathname === "/login",
      `${refused.code}: ${(await refusal.innerText()).slice(0, 120)}`
    )
    const unlocked = await call("POST", `/api/v1/users/${account.id}/unlock`, { token })
    if (unlocked.status !== 204) throw new Inconclusive(`cannot unlock: ${unlocked.status}`)

    // Wrong passwords from a stranger's address: the timed lock of X12.
    for (let attempt = 0; attempt < 5; attempt++) {
      await call("POST", "/api/v1/auth/login", {
        body: { email: account.email, password: "not the password" },
        headers: { "X-Forwarded-For": "203.0.113.77" },
      })
    }
    const before = rowOf(account.id)
    if (before.status !== 3 || before.end !== "future") {
      throw new Inconclusive(`no timed lock after five wrong passwords: ${JSON.stringify(before)}`)
    }
    await clear()
    await page.goto(`${accountsUrl()}/login`)
    await pressPasskey(page)
    await page.waitForURL(left("/login"), { timeout: 30_000 })
    const after = rowOf(account.id)
    check("a timed lock admits the passkey", true, `row before ${JSON.stringify(before)}`)
    check("and moves no count", after.count === before.count && after.status === before.status, `row after ${JSON.stringify(after)}`)
  })
}

// ── passkey-only-admin ──────────────────────────────────────────────────────

async function passkeyOnlyAdmin() {
  const account = await freshAccount("admin", { administrator: true })
  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Admin")
    await clear()
    await withoutSuggestion(page)

    await page.goto(`${accountsUrl()}/login`, { waitUntil: "networkidle" })
    await typePassword(page, account)
    const note = page.getByTestId("passkey-required-note")
    await note.waitFor({ timeout: 30_000 })
    const noted = await note.innerText()
    check("the password shows the API's sentence", noted.length > 0 && new URL(page.url()).pathname === "/login", noted.slice(0, 160))
    const leads = await page.evaluate(() => {
      const button = document.querySelector('[data-testid="passkey-sign-in"] button')
      const address = document.querySelector('input[type="email"]')
      return Boolean(button && address && button.compareDocumentPosition(address) & Node.DOCUMENT_POSITION_FOLLOWING)
    })
    check("and the passkey comes first", leads)

    await pressPasskey(page)
    await page.waitForURL(left("/login"), { timeout: 30_000 })
    await page.goto(`${consoleUrl()}/login`)
    await pressPasskey(page)
    await page.waitForURL(left("/login"), { timeout: 30_000 })
    await page.goto(`${consoleUrl()}/users`)
    await page.locator('[data-slot="sidebar-inset"]').waitFor({ timeout: 30_000 })
    const where = new URL(page.url()).pathname
    check("the passkey signs in with the platform authority", where === "/users", where)

    // Past the window, this account's proof is that passkey, and nothing else.
    const session = newestSession(account.id)
    ageSession(session)
    const card = await openSecurity(page)
    await markDocument(page)
    await card.getByRole("button", { name: "Add a passkey" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await passkeyInWindow(proof).waitFor({ timeout: 30_000 })
    check("its window asks for the passkey alone", (await proof.locator('input[type="password"]').count()) === 0)
    await passkeyInWindow(proof).click()
    await addDialog(page).waitFor({ timeout: 30_000 })
    check("and the addition goes on from it", await sameDocument(page), JSON.stringify(sessionRow(session)))
  })
}

// ── recovery ────────────────────────────────────────────────────────────────

async function recovery() {
  const account = await freshAccount("recovery")
  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Recovery")
    await clear()
    await withoutSuggestion(page)

    const since = sqlNow()
    await call("POST", "/api/v1/auth/deletion/request", { body: { email: account.email } })
    const code = await emailedCode(account.email, since, "account-deletion-verification")
    await call("POST", "/api/v1/auth/deletion/confirm", { body: { email: account.email, otpCode: code } })
    const pending = sql(`SELECT IsDeleted FROM dbo.Users WHERE Id = '${account.id}';`)[0]
    if (pending !== "1") throw new Inconclusive(`the account is not in its grace period: IsDeleted=${pending}`)

    await page.goto(`${accountsUrl()}/login`)
    await pressPasskey(page)
    await page.waitForURL((url) => url.pathname === "/account-recovery", { timeout: 30_000 })
    check("the passkey sign-in goes to the recovery page", true)
    await pressPasskey(page, "Cancel the deletion with your passkey")
    await page.waitForURL(left("/account-recovery"), { timeout: 30_000 })
    const restored = sql(`SELECT IsDeleted FROM dbo.Users WHERE Id = '${account.id}';`)[0]
    check("its passkey choice cancels the deletion, and signs in", restored === "0", `IsDeleted=${restored}, at ${new URL(page.url()).pathname}`)
  })
}

// ── proof-password ──────────────────────────────────────────────────────────

async function proofPassword() {
  const account = await freshAccount("password")
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account)
    const card = await openSecurity(page)
    const session = newestSession(account.id)
    const sessionsBefore = liveSessions(account.id)
    ageSession(session)
    await markDocument(page)
    const startedAt = sqlNow()

    await card.getByRole("button", { name: "Add a passkey" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })

    await proof.getByLabel("Password", { exact: true }).fill("not the password")
    await confirmProof(proof)
    const said = await proof.getByTestId("reauthenticate-message").innerText({ timeout: 30_000 })
    check("a wrong password is refused in the window", said.length > 0, said.slice(0, 120))
    check("and counted, as at the sign-in page", rowOf(account.id).count === 1, JSON.stringify(rowOf(account.id)))
    check("and stamps nothing", sessionRow(session).proof === "none", JSON.stringify(sessionRow(session)))

    await proof.getByLabel("Password", { exact: true }).fill(account.password)
    await confirmProof(proof)
    const add = addDialog(page)
    await add.waitFor({ timeout: 30_000 })
    check("the right password closes the window, and the addition goes on by itself", (await proof.count()) === 0)
    check("nothing reloaded: the person is where they were", await sameDocument(page), page.url())
    check("the same session, and no new one", sessionRow(session).live && liveSessions(account.id) === sessionsBefore, `${sessionsBefore} before, ${liveSessions(account.id)} after`)
    check("the session is stamped with what this proof proved", sessionRow(session).proof === String(STORED.password), JSON.stringify(sessionRow(session)))
    check("a proof clears no count of wrong passwords", rowOf(account.id).count === 1, JSON.stringify(rowOf(account.id)))
    const audited = await auditCount(account.id, startedAt)
    check("audit session.reauthenticated", audited === 1, String(audited))
    const rows = proofRows(account.id, startedAt)
    check("with the address and the browser it came from (F25)", rows.length === 1 && /^1\|\|[^|]+\|agent$/.test(rows[0]), JSON.stringify(rows))

    // A refreshed token keeps its session, and so its proof.
    await page.reload()
    await card.getByRole("button", { name: "Add a passkey" }).click({ timeout: 30_000 })
    await add.waitFor({ timeout: 30_000 })
    check("the proof outlives a reload: nothing is asked again", (await proof.count()) === 0)

    await createInDialog(card, add, account, "InPlace")
    const row = sql(`SELECT CASE WHEN RevokedAt IS NULL THEN 'live' ELSE 'revoked' END FROM dbo.UserPasskeys WHERE UserId = '${account.id}' AND Name = N'InPlace'`)
    check("and the passkey is added", row.length === 1 && row[0] === "live", JSON.stringify(row))
  })
}

// ── proof-passkey ───────────────────────────────────────────────────────────

async function proofPasskey() {
  const account = await freshAccount("again")
  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Again")
    await clear()
    await withoutSuggestion(page)

    // A password session of an account whose only factor is a passkey: adding beside it
    // needs a session that proved two factors, which only the passkey can give (T-C7).
    await signInToConsole(page, account)
    const card = await openSecurity(page)
    const session = newestSession(account.id)
    const sessionsBefore = liveSessions(account.id)
    await markDocument(page)
    await card.getByRole("button", { name: "Add a passkey" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await passkeyInWindow(proof).waitFor({ timeout: 30_000 })
    check("the window names the passkey, and asks for nothing else", (await proof.locator('input[type="password"]').count()) === 0)
    await passkeyInWindow(proof).click()
    const add = addDialog(page)
    await add.waitFor({ timeout: 30_000 })
    check("the passkey goes on from the window, with nothing reloaded", await sameDocument(page), page.url())
    check("in the same session", sessionRow(session).live && liveSessions(account.id) === sessionsBefore, JSON.stringify(sessionRow(session)))
    check("stamped with what it proved", sessionRow(session).proof === String(STORED.passkey), JSON.stringify(sessionRow(session)))

    // Now the device itself is asked, and it holds a passkey of this account already.
    await add.getByRole("button", { name: "Create the passkey" }).click()
    const said = await add.getByTestId("add-passkey-message").innerText({ timeout: 30_000 })
    check("a session that proved two factors reaches the device", /already holds/.test(said), said)
    await add.getByRole("button", { name: "Cancel" }).click()

    await card.getByRole("button", { name: "Remove Again" }).click()
    await page.getByRole("alertdialog").getByRole("button", { name: "Remove", exact: true }).click()
    await card.getByText("No passkeys yet").waitFor({ timeout: 30_000 })
    const row = sql(`SELECT CASE WHEN RevokedAt IS NULL THEN 'live' ELSE 'revoked' END FROM dbo.UserPasskeys WHERE UserId = '${account.id}'`)
    check("removal (DELETE) revokes the row", row.length === 1 && row[0] === "revoked", JSON.stringify(row))
  })
}

// ── proof-app ───────────────────────────────────────────────────────────────

async function proofApp() {
  const account = await freshAccount("app")
  const secret = await bindApp(account)
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account, secret)
    const usedAtSignIn = totp(secret)
    const card = await addPasskey(page, account, "App")
    const session = newestSession(account.id)
    ageSession(session)
    await markDocument(page)

    await card.getByRole("button", { name: "Add a passkey" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await passkeyInWindow(proof).waitFor({ timeout: 30_000 })
    const password = proof.getByLabel("Password", { exact: true })
    const code = proof.getByLabel("Verification code", { exact: true })
    check("the window offers the passkey, the password and the app's code", (await password.count()) === 1 && (await code.count()) === 1)

    const before = sqlNow()
    await password.fill(account.password)
    await code.fill(notTheCode(totp(secret)))
    await confirmProof(proof)
    const said = await proof.getByTestId("reauthenticate-message").innerText({ timeout: 30_000 })
    check("a wrong code is refused, and stamps nothing", sessionRow(session).proof === "none", said.slice(0, 120))
    let failed = []
    for (let attempt = 0; attempt < 20 && failed.length === 0; attempt++) {
      failed = proofRows(account.id, before).filter((row) => row.startsWith("0|"))
      if (failed.length === 0) await sleep(500)
    }
    check(
      "the right password and the wrong code leave a failure row of the proof's action, with its reason and address (F25)",
      failed.length === 1 && /^0\|[A-Za-z]+\.[A-Za-z]+\|[^|]+\|agent$/.test(failed[0]),
      JSON.stringify(failed)
    )
    check("and the password stays, not typed twice", (await password.inputValue()) === account.password)

    await code.fill(await unusedCode(secret, usedAtSignIn))
    await confirmProof(proof)
    const add = addDialog(page)
    await add.waitFor({ timeout: 30_000 })
    check("the password and an unused code go on", await sameDocument(page), JSON.stringify(sessionRow(session)))
    check("stamped with the password and the app", sessionRow(session).proof === String(STORED.passwordAndApp), JSON.stringify(sessionRow(session)))
    await add.getByRole("button", { name: "Cancel" }).click()

    // Past the window again: this proof is old as well, and the passkey alone is enough.
    ageSession(session)
    await card.getByRole("button", { name: "Add a passkey" }).click()
    await proof.waitFor({ timeout: 30_000 })
    await passkeyInWindow(proof).click({ timeout: 30_000 })
    await add.waitFor({ timeout: 30_000 })
    check("past the window again, the passkey alone goes on", sessionRow(session).proof === String(STORED.passkey), JSON.stringify(sessionRow(session)))
    check("still the same document", await sameDocument(page), page.url())
  })
}

// ── proof-disable ───────────────────────────────────────────────────────────

const failedAppCodes = (userId) =>
  Number(sql(`SELECT FailedAttempts FROM dbo.TwoFactorAuth WHERE UserId = '${userId}';`)[0])
const appStillOn = (userId) =>
  sql(`SELECT COUNT(*) FROM dbo.TwoFactorAuth WHERE UserId = '${userId}' AND IsEnabled = 1;`)[0] === "1"

async function proofDisable() {
  // The proof takes the app's code.
  const account = await freshAccount("disable")
  const secret = await bindApp(account)
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account, secret)
    const usedAtSignIn = totp(secret)
    await openSecurity(page)
    const session = newestSession(account.id)
    ageSession(session)
    await markDocument(page)
    const sent = []
    page.on("request", (request) => {
      if (/\/auth\/2fa\/disable$/i.test(new URL(request.url()).pathname) && request.method() === "POST") sent.push(request.postDataJSON())
    })
    const field = page.locator("#disable-code")
    const turnOff = page.getByRole("button", { name: "Turn off the authenticator app" })

    const held = await unusedCode(secret, usedAtSignIn)
    await field.fill(held)
    await turnOff.click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await proof.getByLabel("Password", { exact: true }).fill(account.password)
    await proof.getByLabel("Verification code", { exact: true }).fill(held)
    await confirmProof(proof)
    const told = page.getByText("A code works only once, so enter the next code from your app.")
    await told.waitFor({ timeout: 30_000 })
    check("a proof with the app's code: the held code is not sent again", sent.length === 1, `${sent.length} sent`)
    check("so no failure is counted against the factor", failedAppCodes(account.id) === 0, String(failedAppCodes(account.id)))
    check("the field is empty, and the person is asked for the next code", (await field.inputValue()) === "")
    check("still on, in the same session, on the same page", appStillOn(account.id) && sessionRow(session).live && (await sameDocument(page)), JSON.stringify(sessionRow(session)))

    await field.fill(await unusedCode(secret, held))
    await turnOff.click()
    await page.getByText("The authenticator app is off.").waitFor({ timeout: 30_000 })
    check("the next code turns it off, with nothing signed out", !appStillOn(account.id) && sessionRow(session).live && (await sameDocument(page)), JSON.stringify(sessionRow(session)))
  })

  // The proof takes the passkey: the held code is untouched.
  const other = await freshAccount("disablepk")
  const otherSecret = await bindApp(other)
  await withAuthenticator(async (page) => {
    await signInToConsole(page, other, otherSecret)
    const usedAtSignIn = totp(otherSecret)
    await addPasskey(page, other, "Disable")
    const session = newestSession(other.id)
    ageSession(session)
    await markDocument(page)
    const held = await unusedCode(otherSecret, usedAtSignIn)
    await page.locator("#disable-code").fill(held)
    await page.getByRole("button", { name: "Turn off the authenticator app" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await passkeyInWindow(proof).click({ timeout: 30_000 })
    await page.getByText("The authenticator app is off.").waitFor({ timeout: 30_000 })
    check("a proof with the passkey: the switch-off goes on by itself", !appStillOn(other.id) && (await sameDocument(page)), JSON.stringify(sessionRow(session)))
  })
}

// ── proof-new-codes ─────────────────────────────────────────────────────────

async function proofNewCodes() {
  const account = await freshAccount("newcodes")
  const secret = await bindApp(account)
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account, secret)
    const usedAtSignIn = totp(secret)
    await openSecurity(page)
    const session = newestSession(account.id)
    ageSession(session)
    await markDocument(page)
    const before = sql(`SELECT CHECKSUM(RecoveryCodes) FROM dbo.TwoFactorAuth WHERE UserId = '${account.id}';`)[0]

    await page.getByRole("button", { name: "Generate new codes" }).first().click()
    const dialog = page.getByRole("dialog", { name: "Generate new recovery codes" })
    await dialog.waitFor({ timeout: 30_000 })
    const held = await unusedCode(secret, usedAtSignIn)
    await dialog.getByLabel("Verification code", { exact: true }).fill(held)
    await dialog.getByRole("button", { name: "Generate new codes" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await proof.getByLabel("Password", { exact: true }).fill(account.password)
    await proof.getByLabel("Verification code", { exact: true }).fill(held)
    await confirmProof(proof)

    await proof.waitFor({ state: "hidden", timeout: 30_000 })
    await dialog.waitFor({ timeout: 30_000 })
    check("the dialog stayed under the window, its code field now empty (F26)", (await dialog.getByLabel("Verification code", { exact: true }).inputValue()) === "")
    check("with the next-code sentence", await page.getByText("Confirmed. A code works only once, so enter the next code from your app.").isVisible())
    check("no failure was counted", failedAppCodes(account.id) === 0, String(failedAppCodes(account.id)))
    await dialog.getByLabel("Verification code", { exact: true }).fill(await unusedCode(secret, held))
    await dialog.getByRole("button", { name: "Generate new codes" }).click()
    await dialog.waitFor({ state: "hidden", timeout: 30_000 })
    const after = sql(`SELECT CHECKSUM(RecoveryCodes) FROM dbo.TwoFactorAuth WHERE UserId = '${account.id}';`)[0]
    check("the next code makes new codes, on the same page", after !== before && (await sameDocument(page)))
  })
}

// ── proof-removal ───────────────────────────────────────────────────────────

async function proofRemoval() {
  const account = await freshAccount("removal")
  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Removal")
    await clear()
    await withoutSuggestion(page)
    await signInToConsole(page, account)
    const card = await openSecurity(page)
    const session = newestSession(account.id)
    ageSession(session)
    await markDocument(page)

    await card.getByRole("button", { name: "Remove Removal" }).click({ timeout: 30_000 })
    const confirm = page.getByRole("alertdialog")
    await confirm.getByRole("button", { name: "Remove", exact: true }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await passkeyInWindow(proof).waitFor({ timeout: 30_000 })
    check("a removal's window offers the passkey and the password", (await proof.locator('input[type="password"]').count()) === 1)
    await proof.getByLabel("Password", { exact: true }).fill(account.password)
    await confirmProof(proof)

    await card.getByText("No passkeys yet").waitFor({ timeout: 30_000 })
    check("the removal goes on by itself, with the passkey confirmed, and asks nothing again (F26)", (await proof.count()) === 0 && (await confirm.count()) === 0)
    const row = sql(`SELECT CASE WHEN RevokedAt IS NULL THEN 'live' ELSE 'revoked' END FROM dbo.UserPasskeys WHERE UserId = '${account.id}'`)
    check("and the removal goes on, on the same page", row[0] === "revoked" && (await sameDocument(page)), JSON.stringify(row))
    check("the window closing over the confirmation leaves the page alive", (await page.evaluate(() => document.body.style.pointerEvents)) !== "none")
    await card.getByRole("button", { name: "Add a passkey" }).click({ timeout: 30_000 })
    await addDialog(page).waitFor({ timeout: 30_000 })
    check("the page still answers a click", true)
  })
}

// ── proof-not-the-union ─────────────────────────────────────────────────────

async function proofNotTheUnion() {
  const account = await freshAccount("union")
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Union")
  })

  const signedIn = await apiSignIn(account)
  const { token } = signedIn
  const session = newestSession(account.id)
  // Past the window, and it proved two factors once, as a passkey proof an hour ago records.
  sql(`UPDATE dbo.UserSessions SET StartedAt = DATEADD(MINUTE, -100, SYSUTCDATETIME()), AuthMethods = 97,
    LastProofAt = DATEADD(MINUTE, -100, SYSUTCDATETIME()), LastProofMethods = 96 WHERE Id = '${session}';`)

  const proved = await call("POST", "/api/v1/auth/reauthentication", {
    token,
    body: { password: account.password, emailCode: null, secondStepCode: null, useRecoveryCode: false },
  })
  check("a password proves this person now", proved.status === 204, `${proved.status} ${proved.text.slice(0, 160)}`)
  const row = sessionRow(session)
  check("and records a password, never what the session once proved", row.proof === String(STORED.password) && row.methods === "97", JSON.stringify(row))
  // A proof counts only for the tokens issued after it (F23), as the window refreshes its own.
  const fresh = await signedIn.refresh()
  const change = await call("POST", "/api/v1/auth/passkeys/registration/options", { token: fresh })
  check(
    "so the change that needs two factors still refuses it",
    change.status === 403 && change.json?.code === "Auth.ReauthenticationRequired",
    `${change.status} ${change.json?.code}`
  )
  // A removal needs one recent factor, even beside a passkey (setting up the app there needs two).
  const passkeyId = sql(`SELECT Id FROM dbo.UserPasskeys WHERE UserId = '${account.id}' AND RevokedAt IS NULL;`)[0]
  const one = await call("DELETE", `/api/v1/auth/passkeys/${passkeyId}`, { token: fresh })
  check("while a change that needs one recent factor, a removal, goes on with that token", one.status === 204, `${one.status} ${one.text.slice(0, 200)}`)
}

// ── proof-token-copy ────────────────────────────────────────────────────────

async function proofTokenCopy() {
  const account = await freshAccount("copy")
  await withAuthenticator(async (page) => {
    // Every bearer token the owner's page sends to the API, newest last.
    const sent = []
    page.on("request", (request) => {
      const header = request.headers()["authorization"]
      if (header?.startsWith("Bearer ") && request.url().startsWith(apiUrl())) sent.push(header.slice("Bearer ".length))
    })
    await signInToConsole(page, account)
    const card = await openSecurity(page)
    const session = newestSession(account.id)
    ageSession(session)
    const copy = sent.at(-1)
    if (!copy) throw new Inconclusive("the page sent no bearer token to copy")

    // A second browser context: the copy, without the browser and its refresh cookie, from a
    // page of a first-party origin, as a script that sent the token out would use it.
    const other = await page.context().browser().newContext({ ignoreHTTPSErrors: true })
    try {
      const elsewhere = await other.newPage()
      await elsewhere.goto(`${accountsUrl()}/login`, { waitUntil: "domcontentloaded" })
      const tryTheCopy = () =>
        elsewhere.evaluate(
          async ({ base, bearer }) => {
            const response = await fetch(`${base}/api/v1/auth/passkeys/registration/options`, {
              method: "POST",
              headers: { Authorization: `Bearer ${bearer}` },
            })
            const body = await response.json().catch(() => ({}))
            return { status: response.status, code: body.code }
          },
          { base: apiUrl(), bearer: copy }
        )

      const before = await tryTheCopy()
      check(
        "before the owner's proof, the copy is refused, as the session is old",
        before.status === 403 && before.code === "Auth.ReauthenticationRequired",
        JSON.stringify(before)
      )

      // The owner proves themself where they stand, and their change goes on.
      await markDocument(page)
      await card.getByRole("button", { name: "Add a passkey" }).click()
      const proof = proofWindow(page)
      await proof.waitFor({ timeout: 30_000 })
      await proof.getByLabel("Password", { exact: true }).fill(account.password)
      await confirmProof(proof)
      const add = addDialog(page)
      await add.waitFor({ timeout: 30_000 })
      check("the owner's change goes on from the window", await sameDocument(page), JSON.stringify(sessionRow(session)))
      check("the session is stamped with the owner's proof", sessionRow(session).proof === String(STORED.password), JSON.stringify(sessionRow(session)))
      const owners = sent.at(-1)
      check("with a token the window refreshed after the proof, not the copy", Boolean(owners) && owners !== copy)

      const after = await tryTheCopy()
      check(
        "after the owner's proof, the copy taken before it is still refused (F23)",
        after.status === 403 && after.code === "Auth.ReauthenticationRequired",
        JSON.stringify(after)
      )

      await createInDialog(card, add, account, "Owner")
      const row = sql(`SELECT CASE WHEN RevokedAt IS NULL THEN 'live' ELSE 'revoked' END FROM dbo.UserPasskeys WHERE UserId = '${account.id}' AND Name = N'Owner'`)
      check("and the owner's passkey is added", row.length === 1 && row[0] === "live", JSON.stringify(row))
      const rows = sql(`SELECT COUNT(*) FROM dbo.UserPasskeys WHERE UserId = '${account.id}'`)[0]
      check("and no other passkey was added with the copy", rows === "1", rows)
    } finally {
      await other.close()
    }
  })
}

// ── proof-other-account ─────────────────────────────────────────────────────

async function proofOtherAccount() {
  const mine = await freshAccount("mine")
  const theirs = await freshAccount("theirs")
  await withAuthenticator(async (page) => {
    await signInToConsole(page, theirs)
    await addPasskey(page, theirs, "Theirs")

    const token = await apiSession(mine)
    const session = newestSession(mine.id)
    ageSession(session)
    const hex = sql(`SELECT CONVERT(NVARCHAR(MAX), CredentialId, 2) FROM dbo.UserPasskeys WHERE UserId = '${theirs.id}';`)[0]

    const answer = await page.evaluate(
      async ({ base, bearer, credentialId }) => {
        const begun = await fetch(`${base}/api/v1/auth/passkeys/assertion/options`, { method: "POST", credentials: "include" })
        const { options } = await begun.json()
        const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON({
          ...options,
          allowCredentials: [{ type: "public-key", id: credentialId }],
        })
        const credential = await navigator.credentials.get({ publicKey })
        const response = await fetch(`${base}/api/v1/auth/reauthentication/passkey`, {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json", Authorization: `Bearer ${bearer}` },
          body: JSON.stringify({ credential: credential.toJSON() }),
        })
        const body = await response.json().catch(() => ({}))
        return { status: response.status, code: body.code }
      },
      { base: apiUrl(), bearer: token, credentialId: Buffer.from(hex, "hex").toString("base64url") }
    )
    check("a passkey of another account proves nothing, with its own code", answer.status === 400 && answer.code === "Passkey.NotThisAccount", JSON.stringify(answer))
    check("and stamps nothing", sessionRow(session).proof === "none", JSON.stringify(sessionRow(session)))
    const used = sql(`SELECT CONCAT(ISNULL(CONVERT(NVARCHAR(30), LastUsedAt, 126), 'never'), '|', SignCount) FROM dbo.UserPasskeys WHERE UserId = '${theirs.id}';`)[0]
    check("and that account's passkey shows no use of it (NIT-5)", /^never\|/.test(used ?? ""), used)
  })
}

// ── proof-email ─────────────────────────────────────────────────────────────

async function proofEmail() {
  const account = await freshAccount("mailbox")
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account)
    const session = newestSession(account.id)
    // No password from here on, as an account made by an invitation or a sign-in by code has.
    sql(`UPDATE dbo.Users SET PasswordHash = NULL WHERE Id = '${account.id}';`)
    ageSession(session)
    const card = await openSecurity(page)
    await markDocument(page)

    await card.getByRole("button", { name: "Add a passkey" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    const send = proof.getByRole("button", { name: "Email me a code" })
    await send.waitFor({ timeout: 30_000 })
    check("with no password, the window has no password field", (await proof.locator('input[type="password"]').count()) === 0)

    const since = sqlNow()
    await send.click()
    const code = await emailedCode(account.email, since, "two-factor-email-code")
    const subject = sql(`SELECT TOP 1 Subject FROM dbo.NotificationOutbox WHERE Recipient = N'${account.email}' AND NotificationTypeCode = N'two-factor-email-code' AND CreatedAt >= '${since}' ORDER BY CreatedAt DESC;`)[0]
    check("the email says what its code is for (template version 3)", /confirm it is you/i.test(subject ?? ""), subject)

    const field = proof.getByLabel("Code from the email", { exact: true })
    await field.fill(notTheCode(code))
    await confirmProof(proof)
    const said = await proof.getByTestId("reauthenticate-message").innerText({ timeout: 30_000 })
    check("a wrong code is refused, and stamps nothing", sessionRow(session).proof === "none", said.slice(0, 120))

    await field.fill(code)
    await confirmProof(proof)
    const add = addDialog(page)
    await add.waitFor({ timeout: 30_000 })
    check("the right code goes on, with nothing reloaded", await sameDocument(page), page.url())
    check("stamped with the mailbox as this account's first factor", sessionRow(session).proof === String(STORED.emailCodeFirstFactor), JSON.stringify(sessionRow(session)))
    const own = add.getByRole("button", { name: "Email me a code" })
    await own.waitFor({ timeout: 30_000 })
    check("the addition still asks for its own emailed code (OI-280 is not here)", await own.isVisible())
  })
}

// ── proof-way-out ───────────────────────────────────────────────────────────

async function proofWayOut() {
  const account = await freshAccount("wayout")
  await withAuthenticator(async (page) => {
    await signInToConsole(page, account)
    const session = newestSession(account.id)
    ageSession(session)
    const card = await openSecurity(page)

    await card.getByRole("button", { name: "Add a passkey" }).click()
    const proof = proofWindow(page)
    await proof.waitFor({ timeout: 30_000 })
    await proof.getByRole("button", { name: "Can't confirm it here? Sign out and sign in again" }).click()
    const again = page.getByRole("dialog", { name: "Sign in again to continue" })
    await again.waitFor({ timeout: 30_000 })
    check("the way out asks first: the session is still alive", sessionRow(session).live)

    await again.getByRole("button", { name: "Sign in again" }).click()
    await page.waitForURL((url) => url.pathname === "/login", { timeout: 30_000 })
    let ended = false
    for (let attempt = 0; attempt < 20 && !ended; attempt++) {
      ended = !sessionRow(session).live
      if (!ended) await sleep(500)
    }
    check("signing out ends that session", ended)

    await typePassword(page, account)
    await page.waitForURL((url) => url.pathname === "/profile", { timeout: 30_000 })
    check("and the sign-in comes back to the page", page.url().endsWith("/profile?tab=security"), page.url())
  })
}

// ── proof-unrecorded ────────────────────────────────────────────────────────

async function proofUnrecorded() {
  const account = await freshAccount("unrecorded", { administrator: true })
  const secret = await bindApp(account)
  // Minted before the switch, so it keeps its authority for its 15 minutes: the seeded
  // administrator has no second factor, and could neither set the switch back nor sign in.
  const token = await administratorToken()
  const read = await call("GET", "/api/v1/admin/system-settings", { token })
  const rowVersion = read.json?.sections?.find((section) => section.key === "TwoFactor")?.rowVersion ?? null
  const set = await call("PUT", "/api/v1/admin/system-settings/TwoFactor", {
    token,
    body: { overrides: { EnforceForPlatformAdmins: true }, rowVersion },
  })
  if (set.status !== 200) throw new Inconclusive(`cannot set the switch: ${set.status} ${set.text.slice(0, 200)}`)
  try {
    await withAuthenticator(async (page) => {
      await signInToConsole(page, account, secret)
      const usedAtSignIn = totp(secret)
      const session = newestSession(account.id)
      // A session recorded before methods were, or whose record is gone.
      sql(`UPDATE dbo.UserSessions SET AuthMethods = NULL WHERE Id = '${session}';`)

      await page.goto(`${consoleUrl()}/users`)
      await page.waitForURL((url) => url.pathname === "/two-factor/required", { timeout: 30_000 })
      await page.getByRole("button", { name: "Confirm", exact: true }).click({ timeout: 30_000 })
      const proof = proofWindow(page)
      await proof.waitFor({ timeout: 30_000 })
      await proof.getByLabel("Password", { exact: true }).fill(account.password)
      await proof.getByLabel("Verification code", { exact: true }).fill(await unusedCode(secret, usedAtSignIn))
      await confirmProof(proof)
      await page.waitForURL((url) => url.pathname === "/users", { timeout: 30_000 })
      check("an unrecorded session proves itself in place, and goes where it was going", true, page.url())
      const row = sessionRow(session)
      check("in the same session, now recorded", row.live && row.methods === String(STORED.passwordAndApp), JSON.stringify(row))
      await page.locator('[data-slot="sidebar-inset"]').waitFor({ timeout: 30_000 })
    })
  } finally {
    const reset = await call("POST", "/api/v1/admin/system-settings/TwoFactor/reset", { token })
    if (reset.status !== 200) verdict("the switch is set back", "FAIL", `${reset.status}: drop this database`)
  }
}

// ── before-the-columns ──────────────────────────────────────────────────────

/** /ready caches its answer for a few seconds: asks until it says so, and says what it said. */
async function readyBecomes(expected) {
  let status = "no answer"
  for (let attempt = 0; attempt < 15 && status !== expected; attempt++) {
    const answer = await call("GET", "/ready")
    status = answer.json?.status ?? String(answer.status)
    if (status !== expected) await sleep(1000)
  }
  return status
}

async function beforeTheColumns() {
  const account = await freshAccount("order")
  sql("ALTER TABLE dbo.UserSessions DROP COLUMN LastProofAt, LastProofMethods;")
  try {
    const degraded = await readyBecomes("Degraded")
    check("/ready says Degraded, so a deploy stops there", degraded === "Degraded", degraded)
    const token = await apiSession(account)
    check("with neither column, a sign-in works", Boolean(token))
    const recent = await call("POST", "/api/v1/auth/passkeys/registration/options", { token })
    check("and so does a change on a recent session", recent.status === 200, String(recent.status))

    sql(`UPDATE dbo.UserSessions SET StartedAt = DATEADD(MINUTE, -100, SYSUTCDATETIME()) WHERE Id = '${newestSession(account.id)}';`)
    const stale = await call("POST", "/api/v1/auth/passkeys/registration/options", { token })
    check("a change on a stale session fails closed", stale.status >= 500, String(stale.status))
    const proved = await call("POST", "/api/v1/auth/reauthentication", {
      token,
      body: { password: account.password, emailCode: null, secondStepCode: null, useRecoveryCode: false },
    })
    check("and a proof cannot be stamped", proved.status >= 500, String(proved.status))
  } finally {
    sql("ALTER TABLE dbo.UserSessions ADD LastProofAt DATETIME2 NULL, LastProofMethods INT NULL;")
  }
  const healthy = await readyBecomes("Healthy")
  check("with the columns back, /ready recovers", healthy === "Healthy", healthy)
}

const scenarios = {
  "add-and-sign-in": addAndSignIn,
  origin,
  "app-return": appReturn,
  locks,
  "passkey-only-admin": passkeyOnlyAdmin,
  recovery,
  "proof-password": proofPassword,
  "proof-passkey": proofPasskey,
  "proof-app": proofApp,
  "proof-disable": proofDisable,
  "proof-new-codes": proofNewCodes,
  "proof-removal": proofRemoval,
  "proof-not-the-union": proofNotTheUnion,
  "proof-token-copy": proofTokenCopy,
  "proof-other-account": proofOtherAccount,
  "proof-email": proofEmail,
  "proof-way-out": proofWayOut,
  "proof-unrecorded": proofUnrecorded,
  "before-the-columns": beforeTheColumns,
}
const chosen = process.argv[2] === "all" ? Object.keys(scenarios) : [process.argv[2]]
if (!chosen[0] || chosen.some((name) => !scenarios[name])) {
  console.error(`usage: node Tools/probes/passkey-dev-run.mjs <${[...Object.keys(scenarios), "all"].join("|")}>`)
  process.exit(2)
}
for (const name of chosen) {
  try {
    await scenarios[name]()
  } catch (error) {
    if (error instanceof Inconclusive) verdict(name, "INCONCLUSIVE", error.message)
    else verdict(name, "FAIL", error instanceof Error ? error.message : String(error))
  }
}
if (results.length === 0) {
  console.error("no verdict at all")
  process.exit(1)
}
process.exit(results.every((result) => result.outcome === "PASS") ? 0 : 1)
