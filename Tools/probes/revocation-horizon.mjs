/**
 * Live proof that a sign-out of every device keeps refusing the access tokens issued
 * before it for as long as they can live, across restarts (X04), against the API built
 * from the branch, a THROWAWAY secrets folder and a THROWAWAY database. The xUnit suite
 * pins each rule; this shows the whole path over HTTP and through the database, across
 * the restarts an application pool recycle makes. A check that cannot fail proves
 * nothing, so run it once more against the previous build (the deliberate break below).
 *
 * The probe starts the API itself (PROBE_API_EXE), because what is under test is what a
 * process loads when it starts. The access-token lifetime must be 120 minutes
 * (Jwt__AccessTokenLifetimeMinutes=120 in the API's environment): above an hour, which
 * is where a one-hour revocation stops covering a token. What it does, in order:
 *   1. start: the line `revocation.rehydrated` is written before Kestrel's
 *      "Now listening on", and /ready lists `revocation` Healthy.
 *   2. sign in: access token A is accepted, and lives 120 minutes (exp - iat).
 *   3. sign out of every device with A: A is refused at once.
 *   4. the database: the account's newest user-level row of RevokedTokens is kept
 *      86,700 seconds (24 h 5 min) after it took effect, not 3,600.
 *   5. restart: A is still refused, and a token B signed in after the sign-out is
 *      accepted (the refusal is the revocation, not a broken token path).
 *   6. with --wait: the probe waits until 66 minutes after the sign-out (past the one
 *      hour the previous build kept the entry), then A is refused, and refused again
 *      after one more restart. About 70 minutes.
 *
 * Deliberate break: run the quick mode against a build of the commit before X04
 * (56d86464): it must FAIL at "1 start" (no `revocation` entry, no line) and at
 * "4 database" (3600 seconds). The --wait mode's own break is covered by the unit test
 * that puts the one-hour retention back; it needs no second hour on the old build.
 *
 * Verdicts: PASS · FAIL (a token, the row or /ready answered wrong, a 5xx) ·
 * INCONCLUSIVE (the lifetime is not 120 minutes, or a 429 outlived one wait). Every
 * step is judged and the run goes on, so a failing build shows every difference.
 *
 * Safety. It signs an account out and restarts the API it starts, so it runs only:
 *   - against a localhost API and a local SQL Server (LocalDB included);
 *   - with PROBE_REVOCATION_THROWAWAY=yes, which states that the API runs on a
 *     throwaway secrets folder and a throwaway database.
 * The probe makes its own subject account: a confirmed row with no password, written
 * to the throwaway database, that takes its first password through the API's own
 * "forgot password" and reset. The reset link comes from the API's Serilog file
 * (Email off, Development). No secret, code or token is printed. SQL goes to sqlcmd
 * through a file (-i), never on the command line.
 *
 * The API runs with the probe's own environment, so set its configuration there:
 *   ASPNETCORE_ENVIRONMENT=Development, ASPNETCORE_URLS (the PROBE_API_URL origin),
 *   ConnectionStrings__AuthDb (the throwaway database), SecretManagement__SecretFilePath
 *   and DataProtection__KeyPath (the throwaway folder), Email__Enabled=false,
 *   Jwt__AccessTokenLifetimeMinutes=120, IdentityProvider__SpaRefreshCookieEnabled=false
 *   (the refresh token in the body), Password__BreachedPasswordCheck__Enabled=false.
 *
 * Env: PROBE_API_EXE (the built Auth_API.exe, required), PROBE_API_CONTENT_ROOT
 * (required: a folder holding only copies of the tracked appsettings.json and
 * appsettings.Development.json, so no machine file is read), PROBE_API_URL (default
 * https://localhost:5192, localhost only), PROBE_LOGS_DIR (default
 * <content root>/Logs), PROBE_SQLCMD_SRV (default (localdb)\MSSQLLocalDB, a local server
 * only), PROBE_SQLCMD_DB (required), PROBE_REVOCATION_THROWAWAY=yes (required).
 *
 * Usage: node Tools/probes/revocation-horizon.mjs [--wait]
 * Exit:  0 PASS · 1 FAIL · 4 INCONCLUSIVE · 3 setup aborted · 2 refused
 */
import { execFile, spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { existsSync, mkdtempSync, openSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { promisify } from "node:util";
import { confirmedAccountSql } from "../lib/dev-accounts.mjs";

const execFileAsync = promisify(execFile);
const API = process.env.PROBE_API_URL ?? "https://localhost:5192";
const API_EXE = process.env.PROBE_API_EXE;
const CONTENT_ROOT = process.env.PROBE_API_CONTENT_ROOT;
const LOGS = process.env.PROBE_LOGS_DIR ?? (CONTENT_ROOT ? join(CONTENT_ROOT, "Logs") : null);
const SERVER = process.env.PROBE_SQLCMD_SRV ?? "(localdb)\\MSSQLLocalDB";
const DB = process.env.PROBE_SQLCMD_DB;
const WAIT = process.argv.includes("--wait");
const PASSWORD = `Pr0be-${randomBytes(6).toString("hex")}!Rh`;
const HORIZON_SECONDS = 86_700; // 1,440 minutes + 300 seconds
const LIFETIME_SECONDS = 7_200; // Jwt__AccessTokenLifetimeMinutes=120
const WAIT_PAST_REVOCATION_MS = 66 * 60_000;

// URL.hostname keeps IPv6 brackets ("[::1]", never "::1").
const apiHost = new URL(API).hostname;
if (!["localhost", "127.0.0.1", "[::1]"].includes(apiHost)) {
  console.error(`Refusing to run against ${apiHost}: localhost only.`);
  process.exit(2);
}
const sqlHost = SERVER.replace(/^tcp:/i, "").split(/[\\,]/)[0].toLowerCase();
if (!["localhost", ".", "(local)", "127.0.0.1", "(localdb)"].includes(sqlHost)) {
  console.error(`Refusing to write to SQL Server ${sqlHost}: a local server only.`);
  process.exit(2);
}
if (process.env.PROBE_REVOCATION_THROWAWAY !== "yes") {
  console.error(
    "Refusing to run: this probe signs an account out of every device and restarts the API it starts. " +
      "Set PROBE_REVOCATION_THROWAWAY=yes only for an API running on a throwaway secrets folder and a throwaway database.",
  );
  process.exit(2);
}
if (!API_EXE || !CONTENT_ROOT || !DB) {
  console.error("Set PROBE_API_EXE, PROBE_API_CONTENT_ROOT and PROBE_SQLCMD_DB (see the header).");
  process.exit(2);
}

// The host is checked above, so trust the development certificate for this process only.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

class SetupError extends Error {}
class Inconclusive extends Error {}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const verdicts = [];

function record(name, verdict, detail) {
  verdicts.push({ name, verdict });
  console.log(`${verdict.padEnd(12)} ${name.padEnd(30)} ${detail}`);
}

// One step's checks: every failed expectation is listed. The run goes on either way.
function judge(name, checks, detail) {
  const failed = checks.filter(([ok]) => !ok).map(([, what]) => what);
  record(name, failed.length > 0 ? "FAIL" : "PASS", failed.length > 0 ? failed.join("; ") : detail);
}

// ── The API process ──────────────────────────────────────────────────────────
let api = null;
const consoleLog = join(CONTENT_ROOT, "api-console.log");

async function startApi() {
  const out = openSync(consoleLog, "a");
  const startedAt = existsSync(consoleLog) ? statSync(consoleLog).size : 0;
  api = spawn(API_EXE, ["--contentRoot", CONTENT_ROOT], { cwd: CONTENT_ROOT, env: process.env, stdio: ["ignore", out, out] });
  const exited = new Promise((resolve) => api.once("exit", (code) => resolve(code)));
  for (let waited = 0; waited < 120_000; waited += 1_000) {
    const early = await Promise.race([exited, sleep(1_000).then(() => undefined)]);
    if (early !== undefined) throw new SetupError(`the API exited at start (code ${early}); see api-console.log in the content root`);
    try {
      if ((await fetch(`${API}/.well-known/jwks.json`)).status === 200) {
        return readFileSync(consoleLog, "utf8").slice(startedAt);
      }
    } catch { /* not listening yet */ }
  }
  throw new SetupError("the API did not answer within two minutes");
}

async function stopApi() {
  if (!api) return;
  const exited = new Promise((resolve) => api.once("exit", resolve));
  api.kill();
  await exited;
  api = null;
  await sleep(1_000); // the port is released after the process exits
}

// ── HTTP ─────────────────────────────────────────────────────────────────────
async function call(path, { method = "GET", body, token } = {}) {
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  let payload;
  if (body !== undefined) {
    headers["content-type"] = "application/json";
    payload = JSON.stringify(body);
  }
  const response = await fetch(`${API}${path}`, { method, headers, body: payload, redirect: "manual" });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON body */ }
  return { status: response.status, json, text, code: json?.code ?? null };
}

// A call that meets a rate window waits it out once; a second 429 is INCONCLUSIVE.
async function paced(path, options) {
  const first = await call(path, options);
  if (first.status !== 429) return first;
  await sleep(61_000);
  const second = await call(path, options);
  if (second.status === 429) throw new Inconclusive(`${path} still rate limited after one wait`);
  return second;
}

function must(response, statuses, what) {
  if (!statuses.includes(response.status)) {
    throw new SetupError(`${what}: ${response.status} ${response.code ?? response.text.slice(0, 160)}`);
  }
  return response;
}

// 200 when the API accepts the token, 401 when it refuses it.
async function accepted(jwt) {
  const r = await call("/api/v1/auth/me", { token: jwt });
  if (r.status !== 200 && r.status !== 401) throw new SetupError(`GET me: ${r.status} ${r.code ?? ""}`);
  return r.status === 200;
}

async function readiness() {
  const r = await call("/ready");
  const entry = r.json?.checks?.find((check) => check.name === "revocation");
  return { status: r.status, overall: r.json?.status, revocation: entry?.status ?? null };
}

const claims = (jwt) => JSON.parse(Buffer.from(jwt.split(".")[1], "base64url").toString("utf8"));

// ── Serilog: the reset link the API logs with Email off ─────────────────────
function logMark() {
  if (!existsSync(LOGS)) return { file: null, length: 0 };
  const files = readdirSync(LOGS).filter((f) => f.endsWith(".log")).map((f) => join(LOGS, f));
  if (files.length === 0) return { file: null, length: 0 };
  const file = files.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
  return { file, length: statSync(file).size };
}

async function resetTokenAfter(mark) {
  const pattern = /Password reset link for [^\s:]+:\s*\S*?[?&]token=([^&\s"]+)/g;
  for (let attempt = 0; attempt * 150 < 6_000; attempt++) {
    const file = logMark().file ?? mark.file;
    if (file) {
      const appended = readFileSync(file, "utf8").slice(file === mark.file ? mark.length : 0);
      const matches = [...appended.matchAll(pattern)];
      if (matches.length > 0) return decodeURIComponent(matches[matches.length - 1][1]);
    }
    await sleep(150);
  }
  throw new SetupError("no reset link appeared in the API log (is PROBE_LOGS_DIR the API's Logs directory, and Email off?)");
}

// ── sqlcmd through a file (-i), -b: a T-SQL error is a non-zero exit. The shell profile
//    may export empty SQLCMD* variables that break -E, so clear them. ─────────────────
const sqlEnv = { ...process.env };
delete sqlEnv.SQLCMDUSER;
delete sqlEnv.SQLCMDPASSWORD;
delete sqlEnv.SQLCMDSERVER;
const sqlDir = mkdtempSync(join(tmpdir(), "revocation-horizon-"));

async function sql(query) {
  const file = join(sqlDir, "query.sql");
  writeFileSync(file, `SET NOCOUNT ON;\n${query}\n`);
  const { stdout } = await execFileAsync(
    "sqlcmd",
    ["-S", SERVER, "-d", DB, "-E", "-I", "-b", "-W", "-h", "-1", "-i", file],
    { env: sqlEnv },
  );
  return stdout.trim();
}

const guid = (value) => {
  if (!/^[0-9a-f-]{36}$/i.test(String(value))) throw new SetupError(`not a GUID: ${value}`);
  return value.toLowerCase();
};

// The account's newest user-level revocation: how long it is kept after it took effect.
async function newestUserRowSeconds(userId) {
  const value = await sql(
    "SELECT TOP 1 DATEDIFF(SECOND, [EffectiveAt], [ExpiresAt]) FROM [dbo].[RevokedTokens] " +
      `WHERE [RevocationType] = 3 AND [RevocationKey] = LOWER(N'${guid(userId)}') ORDER BY [CreatedAt] DESC;`,
  );
  return value === "" ? null : Number(value);
}

// ── The account ──────────────────────────────────────────────────────────────
async function account() {
  const email = `rh-${Date.now().toString(36)}${randomBytes(2).toString("hex")}@probe.example.com`;
  const userId = guid(await sql(confirmedAccountSql(email)));
  const mark = logMark();
  must(await paced("/api/v1/auth/forgot-password", { method: "POST", body: { email } }), [200, 202, 204], "forgot-password");
  const resetToken = await resetTokenAfter(mark);
  must(
    await call("/api/v1/auth/reset-password", {
      method: "POST", body: { token: resetToken, newPassword: PASSWORD, confirmNewPassword: PASSWORD, terminateSessions: false },
    }),
    [200, 204],
    "reset-password",
  );
  return { email, userId };
}

async function signIn(who) {
  const r = must(await paced("/api/v1/auth/login", { method: "POST", body: { email: who.email, password: PASSWORD } }), [200], "sign-in");
  const access = r.json?.token?.accessToken;
  if (!access) throw new SetupError("the sign-in returned no access token");
  return access;
}

// Where in a start's console output the load line and Kestrel's first listen line are.
function startOrder(output) {
  const loaded = output.indexOf("revocation.rehydrated");
  const listening = output.indexOf("Now listening on");
  return { loaded, listening };
}

function judgeStart(name, output, ready) {
  const { loaded, listening } = startOrder(output);
  judge(
    name,
    [
      [loaded >= 0, "no revocation.rehydrated line in the start's console output"],
      [loaded < 0 || listening < 0 || loaded < listening, "revocation.rehydrated came after Kestrel's \"Now listening on\""],
      [ready.revocation === "Healthy", `/ready revocation entry: ${ready.revocation ?? "absent"}`],
      [ready.status === 200, `/ready answered ${ready.status} (${ready.overall})`],
    ],
    "revocation.rehydrated before \"Now listening on\"; /ready revocation Healthy",
  );
}

// ── The run ──────────────────────────────────────────────────────────────────
async function main() {
  judgeStart("1 start", await startApi(), await readiness());

  const subject = await account();
  const a = await signIn(subject);
  const { iat, exp } = claims(a);
  if (exp - iat !== LIFETIME_SECONDS) {
    throw new Inconclusive(`the access token lives ${exp - iat} s, not ${LIFETIME_SECONDS}: set Jwt__AccessTokenLifetimeMinutes=120`);
  }
  judge("2 sign in", [[await accepted(a), "token A refused before any sign-out"]], `token A accepted, lives ${exp - iat} s`);

  // iat is in whole seconds: sign out in a later second, so A's iat is strictly before it.
  await sleep(1_100);
  must(await call("/api/v1/auth/logout", { method: "POST", token: a, body: { logoutAllDevices: true } }), [204], "logout-all");
  const signedOutAt = Date.now();
  judge("3 sign out everywhere", [[!(await accepted(a)), "token A still accepted after the sign-out"]], "token A refused at once");

  // The row is written behind the request: give the queue a moment.
  let seconds = null;
  for (let attempt = 0; attempt < 50 && seconds === null; attempt++) {
    seconds = await newestUserRowSeconds(subject.userId);
    if (seconds === null) await sleep(200);
  }
  judge(
    "4 database",
    [[seconds === HORIZON_SECONDS, `DATEDIFF(SECOND, EffectiveAt, ExpiresAt) = ${seconds ?? "no row"}, expected ${HORIZON_SECONDS}`]],
    `DATEDIFF(SECOND, EffectiveAt, ExpiresAt) = ${seconds}`,
  );

  await sleep(1_100);
  const b = await signIn(subject);
  await stopApi();
  const restarted = await startApi();
  judgeStart("5a restart", restarted, await readiness());
  judge(
    "5b after the restart",
    [
      [!(await accepted(a)), "token A accepted again after the restart"],
      [await accepted(b), "token B, signed in after the sign-out, refused"],
    ],
    "token A refused, token B accepted",
  );

  if (WAIT) {
    const due = signedOutAt + WAIT_PAST_REVOCATION_MS;
    console.log(`waiting until ${new Date(due).toISOString()} (66 minutes after the sign-out)`);
    while (Date.now() < due) await sleep(Math.min(60_000, due - Date.now()));
    judge("6a +66 min", [[!(await accepted(a)), "token A accepted 66 minutes after the sign-out"]], "token A refused");
    await stopApi();
    judgeStart("6b restart at +66 min", await startApi(), await readiness());
    judge(
      "6c after it",
      [
        [!(await accepted(a)), "token A accepted after the restart at +66 minutes"],
        [await accepted(b), "token B refused"],
      ],
      "token A refused, token B accepted",
    );
  }
}

let exitCode = 0;
try {
  await main();
  exitCode = verdicts.some((v) => v.verdict === "FAIL") ? 1 : 0;
} catch (error) {
  if (error instanceof Inconclusive) {
    record("run", "INCONCLUSIVE", error.message);
    exitCode = 4;
  } else if (error instanceof SetupError) {
    record("run", "ABORTED", error.message);
    exitCode = 3;
  } else {
    throw error;
  }
} finally {
  await stopApi();
  rmSync(sqlDir, { recursive: true, force: true });
}
console.log(exitCode === 0 ? "PASS" : exitCode === 1 ? "FAIL" : exitCode === 4 ? "INCONCLUSIVE" : "ABORTED");
process.exit(exitCode);
