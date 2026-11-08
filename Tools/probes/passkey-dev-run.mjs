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
 *   - the worktree's DACPAC published to the throwaway LocalDB;
 *   - the worktree's API with Passkeys__Enabled=true and Passkeys__RpId=localhost
 *     in its own environment only (never a settings file), and the probe page's
 *     origin appended to Cors:AllowedOrigins by index (Cors__AllowedOrigins__<n>,
 *     never replacing the list: an array binds by index), not to
 *     IdentityProvider:FirstPartySpaOrigins;
 *   - both apps on https dev servers with a throwaway certificate given through
 *     DEV_HTTPS_CERT / DEV_HTTPS_KEY (never read from .env.*.local).
 *
 * Environment:
 *   PROBE_CONSOLE, PROBE_ACCOUNTS   https://localhost:<port> of each app
 *   PROBE_API                       the API's base URL (https://localhost:<port>)
 *   PROBE_SQL_SERVER, PROBE_DB      the throwaway LocalDB instance and database
 *   PROBE_ADMIN_EMAIL, PROBE_ADMIN_PASSWORD, PROBE_ADMIN_TOTP_SECRET (base32)
 *                                   an account with the authenticator app bound
 *                                   (a passkey added beside a qualifying factor
 *                                   needs a session that proved two factors)
 *   PROBE_OTHER_PORT                the port of the probe's own page (origin scenario)
 *   DEV_HTTPS_CERT, DEV_HTTPS_KEY   the throwaway certificate, for that page
 *
 * Scenarios (positional argument):
 *   add-and-sign-in  Signs in to the console (password, then the app's code),
 *                    adds a passkey in Profile › Security, reads its row (RP ID
 *                    localhost, user-verified, live), the passkey.registered audit
 *                    row and the outbox rows written for the account since; then
 *                    signs in at the accounts app with the passkey button and reads
 *                    the row's LastUsedAt and the user.login audit row.
 *   origin           From a page on PROBE_OTHER_PORT (in CORS, not a first-party
 *                    origin) asks for passkey sign-in options: refused 403
 *                    Auth.FirstPartyOriginRequired.
 *   all              Both, in order.
 *
 * By hand, with the same stack (the steps the owner's PasskeyRealDeviceCheck
 * also walks, with Windows Hello): an administrator's lock refuses the passkey; a
 * timed lock from wrong passwords admits it and moves no count; a passkey-only
 * administrator's password sign-in shows the API's sentence and the passkey
 * button first, then the passkey signs in; an account in its deletion grace
 * period cancels the deletion with its passkey on the recovery page.
 *
 * Verdicts: PASS, FAIL, INCONCLUSIVE (a precondition the probe cannot meet).
 * Nothing it writes outlives the throwaway database: drop it at the end.
 */
import { createHmac } from "node:crypto"
import { execFileSync } from "node:child_process"
import fs from "node:fs"
import https from "node:https"

import { chromium } from "playwright"

const env = (name) => {
  const value = process.env[name]
  if (!value) throw new Inconclusive(`${name} is not set`)
  return value
}

class Inconclusive extends Error {}

function localOnly(url) {
  const { hostname, protocol } = new URL(url)
  if (hostname !== "localhost" || protocol !== "https:") {
    throw new Inconclusive(`${url} is not an https://localhost URL`)
  }
  return url.replace(/\/$/, "")
}

/** RFC 6238: the authenticator app's current code. */
function totp(secretBase32, now = Date.now()) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
  let bits = ""
  for (const char of secretBase32.replace(/[\s=]/g, "").toUpperCase()) {
    const index = alphabet.indexOf(char)
    if (index < 0) throw new Inconclusive("PROBE_ADMIN_TOTP_SECRET is not base32")
    bits += index.toString(2).padStart(5, "0")
  }
  const key = Buffer.from(bits.match(/.{8}/g).map((byte) => parseInt(byte, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(now / 30_000)))
  const digest = createHmac("sha1", key).update(counter).digest()
  const offset = digest[digest.length - 1] & 0x0f
  const code = (digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000
  return String(code).padStart(6, "0")
}

function sql(query) {
  const server = env("PROBE_SQL_SERVER")
  if (!/^\(localdb\)\\/i.test(server)) throw new Inconclusive(`${server} is not a LocalDB instance`)
  return execFileSync("sqlcmd", ["-S", server, "-d", env("PROBE_DB"), "-E", "-h", "-1", "-W", "-s", "|", "-Q", `SET NOCOUNT ON; ${query}`], {
    encoding: "utf8",
  })
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
}

const results = []

function verdict(name, outcome, detail) {
  results.push({ name, outcome, detail })
  console.log(`${outcome.padEnd(12)} ${name}${detail ? ` - ${detail}` : ""}`)
}

async function withAuthenticator(run) {
  const browser = await chromium.launch()
  const context = await browser.newContext({ ignoreHTTPSErrors: true })
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
  try {
    return await run(page)
  } finally {
    await browser.close()
  }
}

async function addAndSignIn() {
  const consoleUrl = localOnly(env("PROBE_CONSOLE"))
  const accountsUrl = localOnly(env("PROBE_ACCOUNTS"))
  const email = env("PROBE_ADMIN_EMAIL")
  const password = env("PROBE_ADMIN_PASSWORD")
  const secret = env("PROBE_ADMIN_TOTP_SECRET")
  // UTC, as the API stores every time; without the zone letter, which DATETIME2 reads as is.
  const startedAt = new Date().toISOString().replace("Z", "")
  const userId = sql(`SELECT CONVERT(NVARCHAR(36), Id) FROM Users WHERE NormalizedEmail = UPPER(N'${email.replace(/'/g, "''")}')`)[0]
  if (!userId) throw new Inconclusive(`no account ${email} in ${env("PROBE_DB")}`)

  await withAuthenticator(async (page) => {
    await page.goto(`${consoleUrl}/login`)
    await page.getByLabel("Email").fill(email)
    await page.getByLabel("Password").fill(password)
    await page.getByRole("button", { name: "Sign in", exact: true }).click()
    await page.getByLabel("Verification code", { exact: true }).fill(totp(secret))
    await page.getByRole("button", { name: "Verify" }).click()
    await page.waitForURL((url) => !/\/(login|two-factor)/.test(url.pathname), { timeout: 30_000 })

    await page.goto(`${consoleUrl}/profile?tab=security`)
    const card = page.getByTestId("passkeys-card")
    await card.waitFor({ timeout: 30_000 })
    await card.getByRole("button", { name: "Add a passkey" }).click()
    const dialog = page.getByRole("dialog", { name: "Add a passkey" })
    await dialog.getByLabel("Name").fill("DevRun")
    await dialog.getByRole("button", { name: "Create the passkey" }).click()
    await card.getByText("DevRun").waitFor({ timeout: 30_000 })

    const row = sql(
      `SELECT RpId, IsUserVerified, CASE WHEN RevokedAt IS NULL THEN 'live' ELSE 'revoked' END FROM UserPasskeys WHERE UserId = '${userId}' AND Name = N'DevRun'`
    )
    row.length === 1 && row[0] === "localhost|1|live"
      ? verdict("registered row", "PASS", row[0])
      : verdict("registered row", "FAIL", JSON.stringify(row))
    const audit = sql(`SELECT COUNT(*) FROM AuditLogs WHERE UserId = '${userId}' AND Action = N'passkey.registered' AND [Timestamp] >= '${startedAt}'`)
    audit[0] === "1" ? verdict("audit passkey.registered", "PASS") : verdict("audit passkey.registered", "FAIL", JSON.stringify(audit))
    const outbox = sql(`SELECT NotificationTypeCode FROM NotificationOutbox WHERE RecipientUserId = '${userId}' AND CreatedAt >= '${startedAt}'`)
    verdict("outbox rows since the start", outbox.length > 0 ? "PASS" : "FAIL", outbox.join(", ") || "none")

    // The accounts app, in the same page: the authenticator is that page's.
    await page.context().clearCookies()
    await page.goto(`${accountsUrl}/login`)
    const passkey = page.getByTestId("passkey-sign-in").getByRole("button", { name: "Sign in with a passkey" })
    await passkey.waitFor({ timeout: 30_000 })
    await passkey.click()
    await page.waitForURL((url) => url.pathname !== "/login", { timeout: 30_000 })
    const used = sql(`SELECT CASE WHEN LastUsedAt >= '${startedAt}' THEN 'used' ELSE 'unused' END FROM UserPasskeys WHERE UserId = '${userId}' AND Name = N'DevRun'`)
    used[0] === "used" ? verdict("passkey sign-in at accounts", "PASS") : verdict("passkey sign-in at accounts", "FAIL", JSON.stringify(used))
    const login = sql(`SELECT TOP 1 Details FROM AuditLogs WHERE UserId = '${userId}' AND Action = N'user.login' AND [Timestamp] >= '${startedAt}' ORDER BY [Timestamp] DESC`)
    verdict("audit user.login", login.length === 1 ? "PASS" : "FAIL", login[0] ?? "none")
  })
}

async function origin() {
  const api = localOnly(env("PROBE_API"))
  const port = Number(env("PROBE_OTHER_PORT"))
  const server = https.createServer(
    { cert: fs.readFileSync(env("DEV_HTTPS_CERT")), key: fs.readFileSync(env("DEV_HTTPS_KEY")) },
    (_, response) => {
      response.writeHead(200, { "content-type": "text/html; charset=utf-8" })
      response.end("<!doctype html><title>not first-party</title>")
    }
  )
  await new Promise((resolve) => server.listen(port, "localhost", resolve))
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
      answer.status === 403 && answer.code === "Auth.FirstPartyOriginRequired"
        ? verdict("another origin is refused", "PASS")
        : verdict("another origin is refused", "FAIL", JSON.stringify(answer))
    })
  } finally {
    server.close()
  }
}

const scenarios = { "add-and-sign-in": addAndSignIn, origin }
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
process.exit(results.every((result) => result.outcome === "PASS") ? 0 : 1)
