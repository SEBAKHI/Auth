/**
 * Live proof that the signing key rotates without signing anyone out, and that an
 * emergency closes a leaked key (S12 PR B), against the API built from the branch,
 * a THROWAWAY secrets file and a THROWAWAY database. The xUnit suite pins each
 * transition; this shows the whole path over HTTP, across the restarts a real
 * rotation takes. A check that cannot fail proves nothing, so run it once more
 * with the deliberate break below: the run must FAIL at "promote".
 *
 * The probe starts the API itself (PROBE_API_EXE), because a staged key enters a
 * process only when it starts: it stops and starts it where an operator recycles
 * the application pool. What it does, in order:
 *   1. start: the key set holds one key, and the keyring panel shows the migrated
 *      key every deployment upgrading to this build starts from (Jwt:KeyId, not yet
 *      saved). With PROBE_SIGNING_UPGRADED_FROM, the key set and the PEM are
 *      byte-identical to what the previous build served on the same secrets file.
 *   2. stage: a new key, in the file only. The key set is unchanged and a promote
 *      is refused (Secret.KeyringNoNextKey): this process never loaded it.
 *   3. restart: the staged key is published after the current one, and its
 *      publication time is recorded. A promote is refused until 24 hours after it
 *      (Secret.KeyringNextTooYoung, naming the time).
 *   4. 24 hours later: PROBE_SIGNING_BACKDATE moves the publication back 25 hours
 *      with the API stopped (the run takes minutes), or else the probe keeps the API
 *      running and waits for the time the refusal named (the run takes a day).
 *   5. promote: an access token signed before it is still accepted, and a refresh
 *      returns a token signed by the new key; the key set lists the new key first
 *      and the old one after it.
 *   6. restart: both tokens are still accepted (the old key's window survives it).
 *   7. emergency: a third key staged, loaded by a restart, promoted in an emergency.
 *      The token the leaked key signed is refused at once, the leaked key leaves the
 *      key set, and a refresh restores the session under the new key.
 *   8. restart: the leaked key stays refused and unpublished; then it is retired.
 * Each keyring change goes through the API's own three steps: a challenge, the
 * emailed code (read from the API's log, Email off) with the change named, and the
 * change itself. Nothing touches the secrets file but the API, and the back-date
 * command between steps 3 and 5 when one is given.
 *
 * Deliberate break: in Auth/Auth.Application/Features/Secrets/PromoteSigningKey/
 * PromoteSigningKeyCommandHandler.cs, pass `null` instead of the scheduled
 * promote's `RsaKeyringChangeService.WithStagedKeyLoaded(...)` (the signer never
 * moves in process), rebuild, restore the secrets file from before the run, run
 * again: the run must FAIL at "promote" (the refreshed token is still signed by
 * the old key), exit 1.
 *
 * Verdicts: PASS · FAIL (a token or the key set answered wrong, a 5xx, an
 * unexpected code) · INCONCLUSIVE (the ring is not the one an upgrading deployment
 * starts from, or a 429 outlived one wait).
 *
 * Safety. This rotates the signing key of the API it starts and ends with an
 * emergency, which refuses every access token the old key signed. So it runs only:
 *   - against a localhost API and a local SQL Server (LocalDB included);
 *   - with PROBE_SIGNING_THROWAWAY=yes, which states that the API runs on a
 *     throwaway secrets file and a throwaway database;
 *   - on a ring that never rotated; else INCONCLUSIVE before any change.
 * The probe makes its own administrator and one subject account: confirmed rows
 * with no password, written to the throwaway database, that take their first
 * password through the API's own "forgot password" and reset (registration/start
 * refuses while email is off, OI-215). The administrator is granted the platform's
 * Super Administrator role there. One-time codes and the reset link come from the
 * API's Serilog file (Email off, Development). No secret, code or token is printed.
 *
 * The API runs with the probe's own environment, so set its configuration there:
 *   ASPNETCORE_ENVIRONMENT=Development, ASPNETCORE_URLS (the PROBE_API_URL origin),
 *   ConnectionStrings__AuthDb (the throwaway database),
 *   SecretManagement__StorageMode=Certificate (the keyring needs an encrypted file),
 *   SecretManagement__SecretFilePath, DataProtection__KeyPath,
 *   DataProtection__Certificate__PfxPath and __PasswordEnvironmentVariable (a
 *   throwaway certificate), SecretManagement__EnableAdminApi=true, Email__Enabled=false,
 *   Email__MaxOtpRequestsPerWindow=20 (five changes would otherwise meet the
 *   per-administrator code limit), IdentityProvider__SpaRefreshCookieEnabled=false
 *   (the refresh token in the body), Password__BreachedPasswordCheck__Enabled=false.
 *
 * Env: PROBE_API_EXE (the built Auth_API.exe, required), PROBE_API_CONTENT_ROOT
 * (required: a folder holding only copies of the tracked appsettings.json and
 * appsettings.Development.json, so no machine file is read), PROBE_API_URL (default
 * https://localhost:5191, localhost only), PROBE_LOGS_DIR (default
 * <content root>/Logs), PROBE_SQLCMD_SRV (default (localdb)\MSSQLLocalDB, a local
 * server only), PROBE_SQLCMD_DB (required), PROBE_SIGNING_THROWAWAY=yes (required),
 * PROBE_SIGNING_BACKDATE (optional: an executable run with the API stopped and the
 * number of hours as its last argument, which moves the staged key's PublishedAtUtc
 * back that many hours in the throwaway secrets file), PROBE_SIGNING_UPGRADED_FROM
 * (optional: a folder holding jwks.json, public-key.pem and jwks.cache-control.txt,
 * saved from the previous build on the same secrets file before this run).
 *
 * Usage: node Tools/probes/signing-key-rotation.mjs
 * Exit:  0 PASS · 1 FAIL · 4 INCONCLUSIVE · 3 setup aborted · 2 refused
 */
import { execFile, spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { existsSync, openSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { promisify } from "node:util";
import { confirmedAccountSql } from "../lib/dev-accounts.mjs";

const execFileAsync = promisify(execFile);
const API = process.env.PROBE_API_URL ?? "https://localhost:5191";
const API_EXE = process.env.PROBE_API_EXE;
const CONTENT_ROOT = process.env.PROBE_API_CONTENT_ROOT;
const LOGS = process.env.PROBE_LOGS_DIR ?? (CONTENT_ROOT ? join(CONTENT_ROOT, "Logs") : null);
const SERVER = process.env.PROBE_SQLCMD_SRV ?? "(localdb)\\MSSQLLocalDB";
const DB = process.env.PROBE_SQLCMD_DB;
const BACKDATE = process.env.PROBE_SIGNING_BACKDATE;
const UPGRADED_FROM = process.env.PROBE_SIGNING_UPGRADED_FROM;
const PASSWORD = `Pr0be-${randomBytes(6).toString("hex")}!Sk`;

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
if (process.env.PROBE_SIGNING_THROWAWAY !== "yes") {
  console.error(
    "Refusing to run: this probe rotates the signing key of the API it starts and ends with an emergency, " +
      "which refuses every access token the old key signed. Set PROBE_SIGNING_THROWAWAY=yes only for an API " +
      "running on a throwaway secrets file and a throwaway database.",
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
class Failed extends Error {}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const verdicts = [];

function record(name, verdict, detail) {
  verdicts.push({ name, verdict });
  console.log(`${verdict.padEnd(12)} ${name.padEnd(34)} ${detail}`);
}

// One step's checks: every failed expectation is listed, then the step FAILs.
function judge(name, checks, detail) {
  const failed = checks.filter(([ok]) => !ok).map(([, what]) => what);
  if (failed.length > 0) {
    record(name, "FAIL", failed.join("; "));
    throw new Failed(name);
  }
  record(name, "PASS", detail);
}

// ── The API process ──────────────────────────────────────────────────────────
let api = null;

async function startApi() {
  // Its console output stays in the content root: in Development it carries codes.
  const out = openSync(join(CONTENT_ROOT, "api-console.log"), "a");
  api = spawn(API_EXE, ["--contentRoot", CONTENT_ROOT], { cwd: CONTENT_ROOT, env: process.env, stdio: ["ignore", out, out] });
  const exited = new Promise((resolve) => api.once("exit", (code) => resolve(code)));
  for (let waited = 0; waited < 120_000; waited += 1_000) {
    const early = await Promise.race([exited, sleep(1_000).then(() => undefined)]);
    if (early !== undefined) throw new SetupError(`the API exited at start (code ${early}); see api-console.log in the content root`);
    try {
      if ((await fetch(`${API}/.well-known/jwks.json`)).status === 200) return;
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

async function restartApi() {
  await stopApi();
  await startApi();
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
  return { status: response.status, json, text, code: json?.code ?? null, headers: response.headers };
}

// A call that meets a rate window waits it out once instead of turning a slow
// run into a false verdict; a second 429 is INCONCLUSIVE.
async function paced(path, options) {
  const first = await call(path, options);
  if (first.status !== 429) return first;
  await sleep(61_000);
  const second = await call(path, options);
  if (second.status === 429) throw new Inconclusive(`${path} still rate limited after one wait`);
  return second;
}

function must(response, statuses, what) {
  if (response.status >= 500) throw new Failed(`${what}: ${response.status} ${response.code ?? ""}`);
  if (!statuses.includes(response.status)) {
    throw new SetupError(`${what}: ${response.status} ${response.code ?? response.text.slice(0, 160)}`);
  }
  return response;
}

// ── Serilog: codes and links the API logs with Email off. The lines mask the
//    address, so capture the file length first and scan only what was appended. ──
function logMark() {
  if (!existsSync(LOGS)) return { file: null, length: 0 };
  const files = readdirSync(LOGS).filter((f) => f.endsWith(".log")).map((f) => join(LOGS, f));
  if (files.length === 0) return { file: null, length: 0 };
  const file = files.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
  return { file, length: statSync(file).size };
}

async function fromLogAfter(mark, pattern, what, waitMs = 6_000) {
  for (let attempt = 0; attempt * 150 < waitMs; attempt++) {
    const file = logMark().file ?? mark.file;
    if (file) {
      const appended = readFileSync(file, "utf8").slice(file === mark.file ? mark.length : 0);
      const matches = [...appended.matchAll(pattern)];
      if (matches.length > 0) return matches[matches.length - 1][1];
    }
    await sleep(150);
  }
  throw new SetupError(`no ${what} appeared in the API log (is PROBE_LOGS_DIR the API's Logs directory, and Email off?)`);
}

const resetTokenAfter = (mark) =>
  fromLogAfter(mark, /Password reset link for [^\s:]+:\s*\S*?[?&]token=([^&\s"]+)/g, "reset link").then(decodeURIComponent);
const secretCodeAfter = (mark) =>
  fromLogAfter(mark, /Secret operation confirmation code for \S+ \(admin [^)]*\):\s*"?(\d{6})"?/g, "confirmation code");

// ── sqlcmd (-b: a T-SQL error is a non-zero exit). The shell profile may export
//    empty SQLCMD* variables that break -E, so clear them. ────────────────────
const sqlEnv = { ...process.env };
delete sqlEnv.SQLCMDUSER;
delete sqlEnv.SQLCMDPASSWORD;
delete sqlEnv.SQLCMDSERVER;

async function sql(query) {
  const { stdout } = await execFileAsync(
    "sqlcmd",
    ["-S", SERVER, "-d", DB, "-E", "-I", "-b", "-W", "-h", "-1", "-s", "|", "-Q", `SET NOCOUNT ON; ${query}`],
    { env: sqlEnv },
  );
  return stdout.trim();
}

const guid = (value) => {
  if (!/^[0-9a-f-]{36}$/i.test(String(value))) throw new SetupError(`not a GUID: ${value}`);
  return value;
};

// ── Accounts: a confirmed row with no password takes its first password through
//    the API's own "forgot password" and reset, then signs in. ─────────────────
const uniqueEmail = (tag) => `sk-${tag}-${Date.now().toString(36)}${randomBytes(2).toString("hex")}@probe.example.com`;

async function login(email) {
  return paced("/api/v1/auth/login", { method: "POST", body: { email, password: PASSWORD } });
}

async function account(tag) {
  const email = uniqueEmail(tag);
  const userId = guid(await sql(confirmedAccountSql(email)));
  const mark = logMark();
  must(await paced("/api/v1/auth/forgot-password", { method: "POST", body: { email } }), [200, 202, 204], `${tag}: forgot-password`);
  const resetToken = await resetTokenAfter(mark);
  must(
    await call("/api/v1/auth/reset-password", {
      method: "POST", body: { token: resetToken, newPassword: PASSWORD, confirmNewPassword: PASSWORD, terminateSessions: false },
    }),
    [200, 204],
    `${tag}: reset-password`,
  );
  return { email, userId };
}

// An access token and its refresh token, from a password sign-in.
async function signIn(who, what) {
  const r = must(await login(who.email), [200], `${what}: sign-in`);
  const token = r.json.token;
  if (!token?.accessToken || !token?.refreshToken) {
    throw new SetupError(`${what}: the sign-in returned no access or no refresh token (is IdentityProvider:SpaRefreshCookieEnabled off?)`);
  }
  return { access: token.accessToken, refresh: token.refreshToken };
}

async function refresh(session, what) {
  const r = must(await call("/api/v1/auth/refresh", { method: "POST", body: { refreshToken: session.refresh } }), [200], `${what}: refresh`);
  const token = r.json.token ?? r.json;
  if (!token?.accessToken || !token?.refreshToken) throw new SetupError(`${what}: the refresh returned no tokens`);
  return { access: token.accessToken, refresh: token.refreshToken };
}

const kidOf = (jwt) => JSON.parse(Buffer.from(jwt.split(".")[0], "base64url").toString("utf8")).kid;

// 200 when the API accepts the token, 401 when it refuses it.
async function accepted(jwt) {
  const r = await call("/api/v1/auth/me", { token: jwt });
  if (r.status !== 200 && r.status !== 401) throw new Failed(`GET me: ${r.status} ${r.code ?? ""}`);
  return r.status === 200;
}

async function jwks() {
  const r = must(await call("/.well-known/jwks.json"), [200], "GET jwks");
  return { text: r.text, kids: r.json.keys.map((k) => k.kid), cacheControl: r.headers.get("cache-control") };
}

// ── Keyring changes, through the three steps the console takes ───────────────
async function adminToken(admin) {
  return (await signIn(admin, "administrator")).access;
}

async function signingRing(admin) {
  const ring = must(await call("/api/v1/admin/Secrets/keyring", { token: await adminToken(admin) }), [200], "GET keyring").json.rsa;
  if (!ring) throw new SetupError("GET keyring carries no rsa member: is this the S12 PR B build?");
  return ring;
}

const changeEndpoint = {
  Stage: () => ["/api/v1/admin/Secrets/keyring/rsa/stage", (challengeId) => ({ challengeId, value: null })],
  Promote: () => ["/api/v1/admin/Secrets/keyring/rsa/promote", (challengeId) => ({ challengeId, emergency: false })],
  PromoteEmergency: () => ["/api/v1/admin/Secrets/keyring/rsa/promote", (challengeId) => ({ challengeId, emergency: true })],
  Retire: (kid) => [`/api/v1/admin/Secrets/keyring/rsa/retire/${encodeURIComponent(kid)}`, (challengeId) => ({ challengeId })],
};

// The challenge alone: a refused change answers here, before any code is emailed.
async function challenge(token, action, kid) {
  return paced("/api/v1/admin/Secrets/challenges", {
    method: "POST",
    token,
    body: { operation: "GenerateRsaKey", value: null, keyringAction: action, kid: action === "Retire" ? kid : undefined },
  });
}

async function change(admin, action, kid) {
  const token = await adminToken(admin);
  const mark = logMark();
  const raised = must(await challenge(token, action, kid), [200, 201], `${action}: challenge`);
  const challengeId = raised.json.challengeId;
  const code = await secretCodeAfter(mark);
  must(
    await call(`/api/v1/admin/Secrets/challenges/${challengeId}/verify`, {
      method: "POST", token, body: { code, keyringAction: action, kid: action === "Retire" ? kid : undefined },
    }),
    [200],
    `${action}: verify`,
  );
  const [path, body] = changeEndpoint[action](kid);
  return must(await call(path, { method: "POST", token, body: body(challengeId) }), [200], action).json;
}

const keyOf = (ring, kid) => ring.keys.find((k) => k.kid === kid);
const hours = (from, to) => (Date.parse(to) - Date.parse(from)) / 3_600_000;

async function main() {
  await startApi();
  try {
    await run();
  } finally {
    await stopApi();
  }
}

async function run() {
  // 1. Start: one migrated key, and, after an upgrade, the very bytes the previous build served.
  const set0 = await jwks();
  const pem0 = must(await call("/.well-known/public-key.pem"), [200], "GET public-key.pem").text;
  if (UPGRADED_FROM) {
    const before = (name) => readFileSync(join(UPGRADED_FROM, name), "utf8");
    judge("upgrade: same key set", [
      [set0.text === before("jwks.json"), "the key set differs from the previous build's"],
      [pem0 === before("public-key.pem"), "the PEM differs from the previous build's"],
      [set0.cacheControl === before("jwks.cache-control.txt").trim(), `Cache-Control "${set0.cacheControl}" differs from the previous build's`],
    ], `${set0.text.length} and ${pem0.length} bytes identical; Cache-Control "${set0.cacheControl}"`);
  }

  const admin = await account("admin");
  // The throwaway database only (PROBE_SIGNING_THROWAWAY): the platform's own Super
  // Administrator role, looked up by name, granted to the account just made.
  await sql(
    `INSERT INTO dbo.UserRoles (Id, UserId, RoleId, ApplicationId, AssignedAt, AssignedBy, IsActive)
     SELECT NEWID(), '${admin.userId}', r.Id, NULL, SYSUTCDATETIME(), '${admin.userId}', 1
     FROM dbo.Roles r WHERE r.Name = N'Super Administrator' AND r.ApplicationId IS NULL;`,
  );
  const subject = await account("subject");

  const ring0 = await signingRing(admin);
  if (!ring0.available || ring0.keys.length !== 1 || !ring0.keys[0].migrated || ring0.persisted) {
    throw new Inconclusive(
      `the signing keyring is not the one an upgrading deployment starts from (available ${ring0.available}, ` +
        `${ring0.keys?.length} keys, persisted ${ring0.persisted}); restore the secrets file from before the run`,
    );
  }
  const k0 = ring0.currentKid;
  judge("start: one migrated key", [
    [set0.kids.length === 1 && set0.kids[0] === k0, `the key set lists ${set0.kids.join(",")}, not ${k0}`],
  ], `kid ${k0}, not yet saved; the key set lists it alone`);
  let session = await signIn(subject, "subject");
  const beforeAll = session.access;

  // 2. Stage: the file only. This process never loaded the key, so a promote is refused.
  const staged = await change(admin, "Stage");
  const k1 = staged.nextKid;
  const set1 = await jwks();
  const early = await challenge(await adminToken(admin), "Promote");
  judge("stage: in the file only", [
    [k1 && k1 !== k0, "no new staged kid"],
    [staged.processNextKid === null || staged.processNextKid === undefined, "the process says it loaded the staged key"],
    [staged.persisted === true, "the ring was not saved"],
    [set1.kids.join() === k0, `the key set lists ${set1.kids.join(",")}`],
    [early.status === 409 && early.code === "Secret.KeyringNoNextKey", `a promote answered ${early.status} ${early.code}`],
  ], `kid ${k1} in the file; key set unchanged; promote refused (Secret.KeyringNoNextKey)`);

  // 3. Restart: published after the current key; a promote waits 24 hours from now.
  await restartApi();
  const set2 = await jwks();
  const ring2 = await signingRing(admin);
  const published = keyOf(ring2, k1)?.publishedAtUtc;
  const tooYoung = await challenge(await adminToken(admin), "Promote");
  judge("restart: staged key published", [
    [set2.kids.join() === [k0, k1].join(), `the key set lists ${set2.kids.join(",")}`],
    [ring2.processNextKid === k1, "the process did not load the staged key"],
    [published && Math.abs(Date.now() - Date.parse(published)) < 5 * 60_000, `publication recorded at ${published}`],
    [ring2.promoteAvailableAtUtc && Math.abs(hours(published, ring2.promoteAvailableAtUtc) - 24) < 0.01, `promote opens at ${ring2.promoteAvailableAtUtc}`],
    [await accepted(beforeAll), "the first token is refused"],
  ], `key set [current ${k0}, staged ${k1}]; publication recorded; first token accepted`);
  judge("promote before 24 hours", [
    [tooYoung.status === 409 && tooYoung.code === "Secret.KeyringNextTooYoung", `a promote answered ${tooYoung.status} ${tooYoung.code}`],
    [typeof tooYoung.json?.detail === "string" && /\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z/.test(tooYoung.json.detail), "the refusal names no time"],
  ], `refused (Secret.KeyringNextTooYoung), naming the time`);

  // 4. A day later: back-dated with the API stopped, or waited for.
  if (BACKDATE) {
    await stopApi();
    const { stdout } = await execFileAsync(BACKDATE, ["25"], { env: process.env });
    await startApi();
    console.log(`${"".padEnd(12)} ${"(publication back-dated)".padEnd(34)} ${stdout.trim().replace(/^\S+: /, "")}`);
  } else {
    const at = Date.parse(ring2.promoteAvailableAtUtc);
    console.log(`waiting until ${ring2.promoteAvailableAtUtc} (set PROBE_SIGNING_BACKDATE to run in minutes)`);
    while (Date.now() < at + 5_000) await sleep(60_000);
  }

  // 5. Promote: nobody signed out. A token the old key signed is accepted; a refresh
  //    returns one the new key signed.
  session = await signIn(subject, "subject");
  const signedByOld = session.access;
  const promoted = await change(admin, "Promote");
  session = await refresh(session, "after promote");
  const signedByNew = session.access;
  const set3 = await jwks();
  const previous = keyOf(promoted, k0);
  judge("promote: nobody signed out", [
    [promoted.currentKid === k1 && promoted.processCurrentKid === k1, `current ${promoted.currentKid}, process ${promoted.processCurrentKid}`],
    [promoted.changeAppliedInProcess === true, "the answer says the change waits for a restart"],
    [kidOf(signedByOld) === k0 && (await accepted(signedByOld)), "the token the old key signed is refused"],
    [kidOf(signedByNew) === k1, `the refreshed token is signed by ${kidOf(signedByNew)}, not ${k1}`],
    [await accepted(signedByNew), "the refreshed token is refused"],
    [set3.kids.join() === [k1, k0].join(), `the key set lists ${set3.kids.join(",")}`],
    [previous?.verifyUntilUtc && hours(new Date().toISOString(), previous.verifyUntilUtc) > 23.9, `the old key is accepted until ${previous?.verifyUntilUtc}`],
  ], `old-key token accepted; refresh signs with ${k1}; key set [${k1}, ${k0}]; old key until ${previous?.verifyUntilUtc}`);

  // 6. Restart: the window survives it.
  await restartApi();
  const set4 = await jwks();
  judge("restart: both keys accepted", [
    [await accepted(signedByOld), "the token the old key signed is refused"],
    [await accepted(signedByNew), "the token the new key signed is refused"],
    [set4.kids.join() === [k1, k0].join(), `the key set lists ${set4.kids.join(",")}`],
  ], `both tokens accepted; key set [${k1}, ${k0}]`);

  // 7. Emergency: the key now signing is taken as leaked.
  const staged2 = await change(admin, "Stage");
  const k2 = staged2.nextKid;
  await restartApi();
  session = await signIn(subject, "subject");
  const signedByLeaked = session.access;
  const emergency = await change(admin, "PromoteEmergency");
  const leakedRefused = !(await accepted(signedByLeaked));
  session = await refresh(session, "after the emergency");
  const set5 = await jwks();
  judge("emergency: leaked key closed", [
    [kidOf(signedByLeaked) === k1, `the leaked-key token is signed by ${kidOf(signedByLeaked)}`],
    [emergency.currentKid === k2 && emergency.changeAppliedInProcess === true, `current ${emergency.currentKid}, applied in process ${emergency.changeAppliedInProcess}`],
    [keyOf(emergency, k1)?.compromised === true, "the leaked key is not marked compromised"],
    [leakedRefused, "the token the leaked key signed is still accepted"],
    [kidOf(session.access) === k2 && (await accepted(session.access)), "the refresh did not restore the session under the new key"],
    [set5.kids[0] === k2 && !set5.kids.includes(k1), `the key set lists ${set5.kids.join(",")}`],
  ], `leaked-key token refused at once; refresh signs with ${k2}; key set without ${k1}`);

  // 8. Restart: the leaked key stays closed; then its record is retired.
  await restartApi();
  const set6 = await jwks();
  const stillRefused = !(await accepted(signedByLeaked));
  const retired = await change(admin, "Retire", k1);
  judge("restart: leaked key stays closed", [
    [stillRefused, "the token the leaked key signed is accepted after the restart"],
    [!set6.kids.includes(k1), `the key set lists ${set6.kids.join(",")}`],
    [!keyOf(retired, k1), "the retired record is still listed"],
  ], `still refused and unpublished after the restart; record retired`);
}

try {
  await main();
} catch (error) {
  if (error instanceof Inconclusive) {
    record("run", "INCONCLUSIVE", error.message);
  } else if (error instanceof Failed) {
    // Recorded by judge, or a 5xx.
    if (!verdicts.some((v) => v.verdict === "FAIL")) record("run", "FAIL", error.message);
  } else if (error instanceof SetupError) {
    console.error(`Setup aborted: ${error.message}`);
    process.exit(3);
  } else {
    console.error(error);
    process.exit(3);
  }
}

const has = (v) => verdicts.some((x) => x.verdict === v);
if (has("FAIL")) {
  console.log("\nFAIL");
  process.exit(1);
}
if (has("INCONCLUSIVE")) {
  console.log("\nINCONCLUSIVE");
  process.exit(4);
}
console.log("\nPASS");
process.exit(0);
