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
 *                       platform authority.
 *   recovery            An account in its deletion grace period: the passkey sign-in goes
 *                       to the recovery page, whose passkey choice cancels the deletion.
 *   sign-in-again       A password session of a passkey-only account adds a passkey: the
 *                       dialog names the passkey, the passkey signs in and comes back
 *                       to the page; the device is then asked. Then the removal.
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
async function call(method, route, { token, body, form, headers = {}, origin = accountsUrl() } = {}) {
  const sent = { Origin: origin, ...headers }
  if (token) sent.Authorization = `Bearer ${token}`
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
  return { status: response.status, json, text }
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

async function addPasskey(page, account, name) {
  await page.goto(`${consoleUrl()}/profile?tab=security`)
  const card = page.getByTestId("passkeys-card")
  await card.waitFor({ timeout: 30_000 })
  await card.getByRole("button", { name: "Add a passkey" }).click()
  const dialog = page.getByRole("dialog", { name: "Add a passkey" })
  await dialog.waitFor()
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

// ── sign-in-again ───────────────────────────────────────────────────────────

async function signInAgain() {
  const account = await freshAccount("again")
  await withAuthenticator(async (page, clear) => {
    await signInToConsole(page, account)
    await addPasskey(page, account, "Again")
    await clear()
    await withoutSuggestion(page)

    // A password session of an account whose only factor is a passkey: adding beside it
    // needs a session that proved two factors, which only the passkey can give (T-C7).
    await signInToConsole(page, account)
    await page.goto(`${consoleUrl()}/profile?tab=security`)
    const card = page.getByTestId("passkeys-card")
    await card.getByRole("button", { name: "Add a passkey" }).click({ timeout: 30_000 })
    await page.getByRole("alertdialog").getByRole("button", { name: "Sign in again with my passkey" }).click()
    await page.waitForURL((url) => url.pathname === "/login", { timeout: 30_000 })
    await pressPasskey(page)
    await page.waitForURL((url) => url.pathname === "/profile", { timeout: 30_000 })
    check("sign in again names the passkey, and comes back to the page", page.url().endsWith("/profile?tab=security"), page.url())

    // Now the device itself is asked, and it holds a passkey of this account already.
    await card.getByRole("button", { name: "Add a passkey" }).click({ timeout: 30_000 })
    const add = page.getByRole("dialog", { name: "Add a passkey" })
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

const scenarios = {
  "add-and-sign-in": addAndSignIn,
  origin,
  "app-return": appReturn,
  locks,
  "passkey-only-admin": passkeyOnlyAdmin,
  recovery,
  "sign-in-again": signInAgain,
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
