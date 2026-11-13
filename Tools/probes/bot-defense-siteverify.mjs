/**
 * Live proof of the bot check against Cloudflare's real siteverify (S13, [S13-O1]): the API
 * built from the branch, on this machine, with Cloudflare's public testing keys. The xUnit
 * suite pins each rule with a stand-in for Cloudflare; this shows the whole path over HTTP,
 * to Cloudflare and back, and the verdict line each request writes.
 *
 * Cloudflare publishes keys for testing (developers.cloudflare.com/turnstile/troubleshooting/
 * testing/): the sitekey 1x00000000000000000000AA makes the dummy token
 * XXXX.DUMMY.TOKEN.XXXX, and three secrets answer it: 1x…AA always passes, 2x…AA always
 * fails, 3x…AA says the token was already spent. No real key and no real visitor are used.
 *
 * The probe starts the API itself, once per scenario, because the secret is read when the
 * process starts. What it checks, in order:
 *   A. secret 1x, Enforce: GET /Platform/bot-defense names the mode and the sitekey with
 *      Cache-Control public, max-age=60; each of the four surfaces without a token answers
 *      400 BotDefense.TokenMissing; a sign-in and a reset request with the dummy token pass
 *      the check (the handler answers, not the check), and the sign-in's verdict line says
 *      Passed after one live call (attempts 1).
 *   B. secret 2x, Enforce: the sign-in answers 400 BotDefense.TokenRejected, the line says
 *      Rejected with Cloudflare's code.
 *   C. secret 3x, Enforce: the same, for a token already spent.
 *   D. a random secret, Enforce, failure mode PerSurface: Cloudflare refuses the secret, so
 *      the check is Misconfigured: registration/start answers BotDefense.Unavailable; five
 *      sign-ins go through the bot-fallback allowance and the sixth answers
 *      BotDefense.Unavailable.
 *   E. secret 2x, Monitor: the same failing token refuses nothing; the line says Rejected.
 * Every step is judged and the run goes on, so a failing build shows every difference.
 *
 * Deliberate break: run scenario A with BotDefense__SiteKey left empty in the environment
 * (PROBE_BREAK=no-sitekey): the four surfaces stop answering TokenMissing and the config
 * names no sitekey, so A must FAIL.
 *
 * Safety. It calls Cloudflare's siteverify (through the API) with public testing keys only,
 * and runs only:
 *   - against an API on localhost that this probe starts;
 *   - with AUTH_DISABLE_DB_SETTINGS=true (a stored setting must not decide the mode) and
 *     Email__Enabled=false (no message leaves the machine) in its environment.
 * Requests name only a fresh s13-probe-…@example.com address, which has no account; the
 * sign-ins and the reset request reach the handlers with it, so the database receives what
 * a failed sign-in by an unknown address writes. No token, secret or code is printed.
 *
 * A request the check lets through is judged by what the check did, not by the handler's
 * answer: the check answers nothing but its own BotDefense.* codes, so any other answer (a
 * 400 for the unknown address, or a 500 from a database behind the branch's schema) shows
 * the request went past it. The status is printed as it came.
 *
 * The API runs with the probe's own environment, so set its configuration there:
 *   ConnectionStrings__AuthDb (the database), SecretManagement__SecretFilePath and
 *   DataProtection__KeyPath (a scratch folder), Email__Enabled=false,
 *   AUTH_DISABLE_DB_SETTINGS=true. The probe sets ASPNETCORE_ENVIRONMENT=Development,
 *   ASPNETCORE_URLS and the four BotDefense values itself.
 *
 * Env: PROBE_API_EXE (the built Auth_API.exe, required), PROBE_API_CONTENT_ROOT (required:
 * a folder holding only copies of the tracked appsettings.json and
 * appsettings.Development.json), PROBE_API_URL (default http://localhost:5196, localhost
 * only), PROBE_ORIGIN (default https://localhost:5174: a first-party origin of the copied
 * Development settings).
 *
 * Usage: node Tools/probes/bot-defense-siteverify.mjs
 * Exit:  0 PASS · 1 FAIL · 3 setup aborted · 2 refused
 */
import { spawn } from "node:child_process"
import { randomBytes } from "node:crypto"
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs"
import { join } from "node:path"

import { parseVerdictLine } from "./verdict-window.mjs"

const SITE_KEY = "1x00000000000000000000AA"
const SECRET = {
  passes: "1x0000000000000000000000000000000AA",
  fails: "2x0000000000000000000000000000000AA",
  spent: "3x0000000000000000000000000000000AA",
}
const DUMMY_TOKEN = "XXXX.DUMMY.TOKEN.XXXX"
const SURFACES = {
  login: "/api/v1/Auth/login",
  register: "/api/v1/Auth/registration/start",
  "forgot-password": "/api/v1/Auth/forgot-password",
  "email-code": "/api/v1/Auth/email-code/start",
}

const results = []
const judge = (scenario, step, pass, detail) => {
  results.push({ scenario, step, pass, detail })
  console.log(`${pass ? "PASS" : "FAIL"}  ${scenario} ${step}: ${detail}`)
}

function refuse(message) {
  console.error(`refused: ${message}`)
  process.exit(2)
}

const exe = process.env.PROBE_API_EXE
const contentRoot = process.env.PROBE_API_CONTENT_ROOT
const apiUrl = new URL(process.env.PROBE_API_URL ?? "http://localhost:5196")
const origin = process.env.PROBE_ORIGIN ?? "https://localhost:5174"
if (!exe || !existsSync(exe)) refuse("PROBE_API_EXE must name the built Auth_API.exe")
if (!contentRoot || !statSync(contentRoot, { throwIfNoEntry: false })?.isDirectory())
  refuse("PROBE_API_CONTENT_ROOT must name a folder with copies of the tracked appsettings files")
if (!["localhost", "127.0.0.1", "[::1]"].includes(apiUrl.hostname)) refuse("PROBE_API_URL must be on localhost")
if (process.env.AUTH_DISABLE_DB_SETTINGS?.toLowerCase() !== "true")
  refuse("set AUTH_DISABLE_DB_SETTINGS=true: a stored setting must not decide the mode")
if (process.env.Email__Enabled?.toLowerCase() !== "false") refuse("set Email__Enabled=false: no message may leave the machine")

const logsDir = join(contentRoot, "Logs")
const address = () => `s13-probe-${randomBytes(6).toString("hex")}@example.com`

/** Every log file's size now, so a step reads only what it caused. */
function logMark() {
  const sizes = new Map()
  if (!existsSync(logsDir)) return sizes
  for (const name of readdirSync(logsDir)) if (name.endsWith(".log")) sizes.set(name, statSync(join(logsDir, name)).size)
  return sizes
}

/** The verdict lines written since `mark`, waiting up to three seconds for `count` of them. */
async function verdictsSince(mark, count = 1) {
  const deadline = Date.now() + 3000
  for (;;) {
    const found = []
    if (existsSync(logsDir)) {
      for (const name of readdirSync(logsDir).filter((file) => file.endsWith(".log")).sort()) {
        const text = readFileSync(join(logsDir, name))
        const added = text.subarray(mark.get(name) ?? 0).toString("utf8")
        for (const line of added.split(/\r?\n/)) {
          const verdict = parseVerdictLine(line)
          if (verdict) found.push(verdict)
        }
      }
    }
    if (found.length >= count || Date.now() > deadline) return found
    await new Promise((resolve) => setTimeout(resolve, 150))
  }
}

async function startApi(botDefense) {
  const environment = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: apiUrl.origin,
    BotDefense__Mode: botDefense.mode,
    BotDefense__FailMode: "PerSurface",
    BotDefense__SiteKey: process.env.PROBE_BREAK === "no-sitekey" ? "" : SITE_KEY,
    BotDefense__TurnstileSecret: botDefense.secret,
  }
  const child = spawn(exe, [], { cwd: contentRoot, env: environment, stdio: ["ignore", "ignore", "ignore"] })
  const deadline = Date.now() + 90_000
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error(`the API exited with ${child.exitCode} before it answered`)
    try {
      const response = await fetch(new URL("/api/v1/Platform/bot-defense", apiUrl))
      if (response.ok) return child
    } catch {
      // Not listening yet.
    }
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  child.kill()
  throw new Error("the API did not answer within 90 seconds")
}

async function stopApi(child) {
  if (child.exitCode !== null) return
  const exited = new Promise((resolve) => child.once("exit", resolve))
  child.kill()
  await exited
}

async function post(path, body) {
  const response = await fetch(new URL(path, apiUrl), {
    method: "POST",
    headers: { "Content-Type": "application/json", Origin: origin },
    body: JSON.stringify(body),
  })
  const text = await response.text()
  let json
  try {
    json = text ? JSON.parse(text) : undefined
  } catch {
    json = undefined
  }
  return { status: response.status, code: json?.code }
}

function bodyFor(surface, email, token) {
  const withToken = token ? { botToken: token } : {}
  if (surface === "login") return { email, password: "not-the-password-1A!", staySignedIn: false, ...withToken }
  if (surface === "forgot-password") return { email, ...withToken }
  return { email, preferredLanguage: "en", ...withToken }
}

const isBotCode = (code) => typeof code === "string" && code.startsWith("BotDefense.")
const show = (answer) => `${answer.status} ${answer.code ?? "(no code)"}`

async function scenario(name, botDefense, run) {
  console.log(`\n${name}: secret ${botDefense.label}, mode ${botDefense.mode}`)
  let child
  try {
    child = await startApi(botDefense)
  } catch (error) {
    judge(name, "start", false, error.message)
    return
  }
  try {
    await run()
  } catch (error) {
    judge(name, "run", false, `stopped: ${error.message}`)
  } finally {
    await stopApi(child)
  }
}

await scenario("A", { label: "1x (always passes)", mode: "Enforce", secret: SECRET.passes }, async () => {
  const config = await fetch(new URL("/api/v1/Platform/bot-defense", apiUrl))
  const published = await config.json()
  const cache = config.headers.get("cache-control") ?? ""
  judge(
    "A",
    "config",
    published.mode === "Enforce" && published.siteKey === SITE_KEY && /\bpublic\b/.test(cache) && /\bmax-age=60\b/.test(cache),
    `mode ${published.mode}, sitekey ${published.siteKey === SITE_KEY ? "the testing one" : String(published.siteKey)}, Cache-Control "${cache}"`
  )
  for (const [surface, path] of Object.entries(SURFACES)) {
    const answer = await post(path, bodyFor(surface, address(), undefined))
    judge("A", `${surface} without a token`, answer.status === 400 && answer.code === "BotDefense.TokenMissing", show(answer))
  }
  const mark = logMark()
  const signIn = await post(SURFACES.login, bodyFor("login", address(), DUMMY_TOKEN))
  judge("A", "login with the dummy token", !isBotCode(signIn.code), `${show(signIn)} (the handler answered, not the check)`)
  const [line] = await verdictsSince(mark)
  judge(
    "A",
    "its verdict line",
    line?.verdict === "Passed" && line.action === "login" && line.attempts === 1,
    line
      ? `verdict ${line.verdict}, action ${line.action}, origin ${line.origin}, hostname ${line.hostname}, attempts ${line.attempts}, ${line.elapsedMs} ms`
      : "no verdict line in the log"
  )
  const reset = await post(SURFACES["forgot-password"], bodyFor("forgot-password", address(), DUMMY_TOKEN))
  judge("A", "forgot-password with the dummy token", !isBotCode(reset.code), `${show(reset)} (the handler answered, not the check)`)
})

for (const [name, label, secret, code] of [
  ["B", "2x (always fails)", SECRET.fails, "fails"],
  ["C", "3x (token already spent)", SECRET.spent, "already spent"],
]) {
  await scenario(name, { label, mode: "Enforce", secret }, async () => {
    const mark = logMark()
    const answer = await post(SURFACES.login, bodyFor("login", address(), DUMMY_TOKEN))
    judge(name, "login with the dummy token", answer.status === 400 && answer.code === "BotDefense.TokenRejected", show(answer))
    const [line] = await verdictsSince(mark)
    judge(
      name,
      "its verdict line",
      line?.verdict === "Rejected" && line.codes !== "-",
      line ? `verdict ${line.verdict}, Cloudflare's codes ${line.codes} (${code})` : "no verdict line in the log"
    )
  })
}

await scenario("D", { label: "random (Cloudflare refuses it)", mode: "Enforce", secret: randomBytes(18).toString("hex") }, async () => {
  const registration = await post(SURFACES.register, bodyFor("register", address(), DUMMY_TOKEN))
  judge("D", "registration/start", registration.status === 400 && registration.code === "BotDefense.Unavailable", show(registration))
  const email = address()
  const mark = logMark()
  const answers = []
  for (let attempt = 1; attempt <= 6; attempt += 1) answers.push(await post(SURFACES.login, bodyFor("login", email, DUMMY_TOKEN)))
  judge(
    "D",
    "five sign-ins through the allowance",
    answers.slice(0, 5).every((answer) => !isBotCode(answer.code)),
    answers.slice(0, 5).map(show).join(", ")
  )
  judge("D", "the sixth sign-in", answers[5].status === 400 && answers[5].code === "BotDefense.Unavailable", show(answers[5]))
  const lines = await verdictsSince(mark, 6)
  judge(
    "D",
    "the verdict lines",
    lines.length >= 6 && lines.every((line) => line.verdict === "Misconfigured"),
    lines.map((line) => `${line.verdict} ${line.codes}`).join("; ") || "no verdict line in the log"
  )
})

await scenario("E", { label: "2x (always fails)", mode: "Monitor", secret: SECRET.fails }, async () => {
  const mark = logMark()
  const answer = await post(SURFACES.login, bodyFor("login", address(), DUMMY_TOKEN))
  judge("E", "login with the failing token", !isBotCode(answer.code), `${show(answer)} (Monitor refuses nothing)`)
  const [line] = await verdictsSince(mark)
  judge(
    "E",
    "its verdict line",
    line?.verdict === "Rejected" && line.mode === "Monitor",
    line ? `verdict ${line.verdict}, mode ${line.mode}` : "no verdict line in the log"
  )
})

const failed = results.filter((result) => !result.pass)
console.log(`\n${results.length - failed.length} of ${results.length} steps passed.`)
process.exitCode = failed.length === 0 && results.length > 0 ? 0 : 1
