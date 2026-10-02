/**
 * Live proof that the second-factor guessing limits hold when requests truly
 * overlap, against a running dev API and the dev database. The SQL guards in
 * SecondFactorAtomicitySqlTests pin the statements; this shows what SQL Server
 * does with them under concurrency. A check that cannot fail proves nothing, so
 * run this against the unchanged base build and against each deliberate break
 * before trusting a PASS: every one of those must FAIL or report INCONCLUSIVE.
 *
 * Scenarios (positional argument):
 *   burst         one challenge, 15 wrong TOTP codes at once.
 *                 PASS: exactly 5 User.InvalidTwoFactorCode, the other 10
 *                 TwoFactor.LockedOut, FailedAttempts = 5, the factor locked.
 *   recovery      two live challenges, one recovery code on both at once.
 *                 PASS: exactly one 200, the loser TwoFactor.ChallengeInvalid
 *                 (lost the commit, not the check), 9 of 10 codes left.
 *   verify-email  an unconfirmed account, 15 wrong email codes at once.
 *                 PASS: AttemptCount = 5, more than 5 InvalidOrExpiredOtp answers,
 *                 the address still unconfirmed and the token unspent.
 *   totp-replay   one correct authenticator-app code is accepted once (X01), in
 *                 four parts on one account:
 *                 (a) a fresh code signs in (200); the same code on a new
 *                     challenge answers TwoFactor.CodeAlreadyUsed;
 *                 (b) two live challenges, the same fresh code on both at once:
 *                     exactly one 200, the other CodeAlreadyUsed. The barrier
 *                     proves both requests were in flight together (held at their
 *                     first write, the attempt reservation; else INCONCLUSIVE);
 *                     it cannot hold them at the claim itself, whose atomicity
 *                     rests on its being one statement (SecondFactorAtomicitySqlTests);
 *                 (c) the code of step s after the code of step s+1: refused;
 *                 (d) TwoFactor:RejectReusedCodes switched off through the System
 *                     Settings API: the same code twice now signs in twice and the
 *                     API logs the accepted reuse — no restart. The probe switches
 *                     it back (and prints how, should it die first). Needs
 *                     PROBE_ADMIN_EMAIL and PROBE_ADMIN_PASSWORD (an account with
 *                     system-settings:manage and no second factor), else
 *                     INCONCLUSIVE.
 *                 The build before X01 must FAIL (a), (b) and (c): it accepts the
 *                 reused code.
 *   lifecycle-disable  switching two-factor off signs the other devices out (X02).
 *                 A second device signs in with the password and a code; the
 *                 first switches two-factor off with a newer code.
 *                 PASS: disable 204; the second device's refresh token answers
 *                 Auth.RefreshTokenRevoked while the first device's still renews;
 *                 two two-factor-changed notices in the outbox (on, then off — so
 *                 the dev database needs seed 0021); flag 0 and no factor row.
 *                 The build before X02 must FAIL: the other device renews.
 *   lifecycle-enable   ten enables of one pending factor with one correct code at
 *                 once (X02). PASS: exactly one 200, the others
 *                 User.TwoFactorAlreadyEnabled or TwoFactor.LockedOut (the attempt
 *                 reservation counts concurrent tries too); the factor and the
 *                 account flag both on; the winner's recovery code signs in; all
 *                 ten held at the barrier together. The build before X02 must
 *                 FAIL: several enables succeed, each showing codes of its own.
 *   all           every scenario that runs with AUTH_DISABLE_DB_SETTINGS=true —
 *                 burst, recovery, verify-email, lifecycle-disable, lifecycle-enable
 *                 — 61 s apart (the "login" window is 20 req / 60 s / IP).
 *                 totp-replay is NOT part of it: it needs that variable unset.
 *
 * Overlap is forced the same way on every build. Two things are needed, because
 * the dev database runs READ COMMITTED without snapshot isolation, so contenders
 * on one hot row trickle through one at a time unless they arrive together:
 *   1. warm-up: the account fires the same count of authenticated reads first, so
 *      the API's SQL connection pool is already open (Min Pool Size helps too).
 *   2. synchronized release: each request goes on its own raw TLS socket with the
 *      last body byte withheld, so ASP.NET model binding — and therefore the
 *      handler — cannot start until that byte arrives. Releasing the byte for all
 *      sockets at once starts every handler within microseconds. (A pooled HTTP
 *      client funnels streamed bodies onto one connection and serializes them,
 *      which is why this opens the sockets itself.)
 *   3. barrier: a separate sqlcmd session holds an update lock (UPDLOCK, ROWLOCK)
 *      on every row the scenario writes, inside an open transaction. Reads pass
 *      (shared is compatible with update), so every request gets through its reads
 *      and queues on its first write; the probe confirms all of them are blocked
 *      (peak = N) before committing the barrier to release the race.
 *
 * totp-replay needs the API to read DB-backed settings, because part (d) flips
 * one: run that scenario WITHOUT AUTH_DISABLE_DB_SETTINGS. It paces its
 * sequential sign-ins (one wait on a 429) and waits for fresh TOTP steps between
 * parts, so it takes two to three minutes.
 *
 * Verdicts (anything but PASS blocks the merge):
 *   PASS         outcome right AND the answers prove the requests overlapped
 *   FAIL         outcome wrong, a 5xx, or an unexpected code
 *   INCONCLUSIVE overlap not proven (not all requests reached the barrier, or the
 *                answers show they ran one at a time), or a 429 (window not fresh)
 *
 * Safety: a localhost API and a local SQL Server only. Throwaway accounts made
 * through the API's own flows; one-time codes read from the API's Serilog file
 * (Email disabled in dev); no stored secret decrypted; no secret or code printed;
 * every setup step checked (sqlcmd -b); revert SQL printed, removing child rows first.
 *
 * Env: PROBE_API_URL (default https://localhost:5201, localhost only), PROBE_LOGS_DIR
 * (default Auth/Auth_API/Logs), PROBE_SQLCMD_SRV (default localhost\SQLEXPRESS01, a
 * local server only), PROBE_SQLCMD_DB (Astoom_Auth). totp-replay (d) only:
 * PROBE_ADMIN_EMAIL and PROBE_ADMIN_PASSWORD, read from the environment and never
 * printed. Run the API with AUTH_DISABLE_DB_SETTINGS=true for burst, recovery,
 * verify-email and the two lifecycle scenarios, so the rate window is the one
 * assumed. The lifecycle scenarios read refresh tokens from response bodies, so
 * the API must not move them into cookies for a request without a first-party
 * Origin (it does not).
 *
 * Usage: node Tools/probes/two-factor-race.mjs <burst|recovery|verify-email|totp-replay|lifecycle-disable|lifecycle-enable|all>
 * Exit:  0 all PASS · 1 a FAIL · 4 an INCONCLUSIVE (no FAIL) · 3 setup aborted · 2 refused
 */
import { createHmac } from "node:crypto";
import { execFile, spawn } from "node:child_process";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { connect as tlsConnect } from "node:tls";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const HERE = dirname(fileURLToPath(import.meta.url));
const API = process.env.PROBE_API_URL ?? "https://localhost:5201";
const LOGS = process.env.PROBE_LOGS_DIR ?? resolve(HERE, "..", "..", "Auth", "Auth_API", "Logs");
const SERVER = process.env.PROBE_SQLCMD_SRV ?? "localhost\\SQLEXPRESS01";
const DB = process.env.PROBE_SQLCMD_DB ?? "Astoom_Auth";
const RACERS = 15;
const PASSWORD = `Pr0be-${Math.random().toString(36).slice(2, 10)}!Zq`;
// totp-replay (d) only. Read from the environment, never from a file: a password
// in a tracked file is published to everyone who can read the repository.
const ADMIN = { email: process.env.PROBE_ADMIN_EMAIL, password: process.env.PROBE_ADMIN_PASSWORD };

// URL.hostname keeps IPv6 brackets ("[::1]", never "::1").
const apiHost = new URL(API).hostname;
if (!["localhost", "127.0.0.1", "[::1]"].includes(apiHost)) {
  console.error(`Refusing to run against ${apiHost}: localhost only.`);
  process.exit(2);
}
const sqlHost = SERVER.replace(/^tcp:/i, "").split(/[\\,]/)[0].toLowerCase();
if (!["localhost", ".", "(local)", "127.0.0.1"].includes(sqlHost)) {
  console.error(`Refusing to write to SQL Server ${sqlHost}: a local server only.`);
  process.exit(2);
}

// The host is checked above, so trust the dev certificate for this process only.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

class SetupError extends Error {}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const accounts = [];
const verdicts = [];

// ── TOTP (RFC 6238: SHA1, 30 s, 6 digits): one valid code to enable 2FA, and
//    wrong codes kept clear of the ±1 window the server accepts. ────────────
function base32Decode(secret) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const ch of secret.replace(/=+$/, "").toUpperCase()) {
    const value = alphabet.indexOf(ch);
    if (value >= 0) bits += value.toString(2).padStart(5, "0");
  }
  const bytes = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}

function hotp(key, counter) {
  const buf = Buffer.alloc(8);
  buf.writeBigUInt64BE(BigInt(counter));
  const hmac = createHmac("sha1", key).update(buf).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  const truncated =
    ((hmac[offset] & 0x7f) << 24) | (hmac[offset + 1] << 16) | (hmac[offset + 2] << 8) | hmac[offset + 3];
  return (truncated % 1_000_000).toString().padStart(6, "0");
}

const step = () => Math.floor(Date.now() / 1000 / 30);
const codeAt = (secret, at) => hotp(base32Decode(secret), at);
const totpNow = (secret) => codeAt(secret, step());

// The server accepts the current step and one either side. Once the clock is
// past `after`, step() + 1 is accepted and newer than any step at or below
// `after` — a step no earlier part of a run has spent.
async function freshStepAfter(after) {
  while (step() <= after) await sleep(500);
  return step() + 1;
}

function codesExcept(excluded, count) {
  const codes = [];
  for (let n = 0; codes.length < count; n++) {
    const candidate = n.toString().padStart(6, "0");
    if (!excluded.has(candidate)) codes.push(candidate);
  }
  return codes;
}

const wrongTotpCodes = (secret, count) =>
  codesExcept(new Set([-1, 0, 1].map((d) => hotp(base32Decode(secret), step() + d))), count);

// ── HTTP ─────────────────────────────────────────────────────────────────────
async function call(path, { method = "GET", body, token } = {}) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: { "content-type": "application/json", ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON body */ }
  return { status: response.status, json, text };
}

// A sequential call that meets the "login" rate window waits it out once,
// instead of turning a slow run into a false verdict. Races never retry. The
// wait is remembered: it can move the clock past a code computed before it.
let pacedWaits = 0;
async function callPaced(path, options) {
  const first = await call(path, options);
  if (first.status !== 429) return first;
  pacedWaits += 1;
  await sleep(61_000);
  return call(path, options);
}

// One race request on its own raw TLS socket, with the last body byte withheld
// until `gate` opens. ASP.NET model binding of a [FromBody] parameter waits for
// the whole Content-Length, so the handler cannot start until that byte arrives;
// releasing the gate lets all of them start together. A dedicated socket per
// request sidesteps the HTTP client pooling that otherwise funnels a streamed
// body onto one connection and serializes the requests. Returns {status, code,
// json}: json is the parsed body of a success, null otherwise.
function rawPost(path, bodyObject, gate, token) {
  const payload = Buffer.from(JSON.stringify(bodyObject), "utf8");
  const head =
    `POST ${path} HTTP/1.1\r\nHost: ${apiHost}\r\nContent-Type: application/json\r\n` +
    (token ? `Authorization: Bearer ${token}\r\n` : "") +
    `Content-Length: ${payload.length}\r\nConnection: close\r\n\r\n`;
  const url = new URL(API);

  return new Promise((resolve) => {
    const socket = tlsConnect({ host: url.hostname, port: Number(url.port), servername: "localhost", rejectUnauthorized: false }, () => {
      socket.write(head);
      if (payload.length > 1) socket.write(payload.subarray(0, payload.length - 1));
      gate.then(() => socket.write(payload.subarray(payload.length - 1)));
    });
    const chunks = [];
    socket.on("data", (d) => chunks.push(d));
    socket.on("error", (error) => resolve({ status: 0, code: `NETWORK ${error.message}` }));
    socket.on("end", () => {
      const raw = Buffer.concat(chunks);
      const text = raw.toString("utf8");
      const status = Number(text.match(/^HTTP\/1\.1 (\d{3})/)?.[1] ?? 0);
      // The body may be chunked (Content-Length is not guaranteed), so read the
      // error code out of the whole response rather than parsing a framed body.
      const code = text.match(/"code"\s*:\s*"([^"]+)"/)?.[1];
      const ok = status > 0 && status < 300;
      resolve({ status, code: ok ? "200" : code ?? `HTTP ${status}`, json: ok ? bodyJson(raw) : null });
    });
  });
}

// The JSON body of a raw HTTP/1.1 response, de-chunked when it is chunked; null
// when there is none or it does not parse.
function bodyJson(raw) {
  const split = raw.indexOf("\r\n\r\n");
  if (split < 0) return null;
  const headers = raw.subarray(0, split).toString("latin1");
  let body = raw.subarray(split + 4);
  if (/^transfer-encoding:\s*chunked/im.test(headers)) {
    const parts = [];
    let at = 0;
    for (;;) {
      const lineEnd = body.indexOf("\r\n", at);
      if (lineEnd < 0) break;
      const size = parseInt(body.subarray(at, lineEnd).toString("latin1"), 16);
      if (!size) break;
      parts.push(body.subarray(lineEnd + 2, lineEnd + 2 + size));
      at = lineEnd + 2 + size + 2;
    }
    body = Buffer.concat(parts);
  }
  try { return body.length ? JSON.parse(body.toString("utf8")) : null; } catch { return null; }
}

// ── Serilog: read a one-time code the API logged. The line masks the address,
//    so capture the file length first and scan only what the call appended. ──
function logMark() {
  const files = readdirSync(LOGS).filter((f) => f.endsWith(".log")).map((f) => join(LOGS, f));
  if (files.length === 0) return { file: null, length: 0 };
  const file = files.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
  return { file, length: statSync(file).size };
}

async function otpAfter(mark) {
  for (let attempt = 0; attempt < 40; attempt++) {
    const file = logMark().file ?? mark.file;
    if (file) {
      const appended = readFileSync(file, "utf8").slice(file === mark.file ? mark.length : 0);
      // The dev file template prints properties unquoted (OTP for x****2@host: 880698);
      // quotes tolerated in case it changes.
      const matches = [...appended.matchAll(/OTP for "?[^\s":]+"?:\s*"?(\d{6})"?/g)];
      if (matches.length > 0) return matches[matches.length - 1][1];
    }
    await sleep(150);
  }
  throw new SetupError("no OTP appeared in the API log (is PROBE_LOGS_DIR the API's Logs directory?)");
}

// ── sqlcmd. -b makes any T-SQL error a non-zero exit, so a failed setup aborts
//    instead of passing silently. The shell profile exports empty SQLCMD* vars
//    that break -E, so clear them. ───────────────────────────────────────────
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

// A setup read that must return one scalar, or the run aborts.
async function sqlScalar(query, what) {
  const value = await sql(query);
  if (value === "" || /^Msg \d+/m.test(value)) throw new SetupError(`${what}: ${value || "no row"}`);
  return value;
}

// ── The barrier: a held transaction that U-locks the rows a scenario writes,
//    so every racing request blocks on its first write until the barrier lets
//    go. `lockSql` takes an update lock (UPDLOCK): readers pass, because a shared
//    lock is compatible with it, while every writer — which needs an update or
//    exclusive lock on the same row — waits. ─────────────────────────────────
class Barrier {
  #child;
  #spid;

  async hold(lockSql) {
    this.#child = spawn("sqlcmd", ["-S", SERVER, "-d", DB, "-E", "-I", "-b", "-h", "-1", "-W"], {
      env: sqlEnv,
      stdio: ["pipe", "pipe", "pipe"],
    });
    let out = "";
    this.#child.stdout.on("data", (d) => (out += d.toString()));
    this.#child.stderr.on("data", (d) => (out += `[stderr]${d}`));
    this.#child.stdin.write(`SET NOCOUNT ON; BEGIN TRAN; ${lockSql} SELECT CONCAT('SPID:', @@SPID);\nGO\n`);

    const started = Date.now();
    while (Date.now() - started < 10_000) {
      const m = out.match(/SPID:(\d+)/);
      if (m) { this.#spid = Number(m[1]); break; }
      if (/\[stderr\]|Msg \d+/.test(out)) throw new SetupError(`barrier failed to take the lock: ${out}`);
      await sleep(25);
    }
    if (!this.#spid) throw new SetupError("barrier did not report its session id");
    // Confirm the lock is actually held.
    await sqlScalar(
      `SELECT 1 WHERE EXISTS (SELECT 1 FROM sys.dm_tran_session_transactions WHERE session_id = ${this.#spid})`,
      "barrier transaction",
    );
  }

  // How many sessions are blocked waiting on the barrier's session right now,
  // following the wait chain transitively (a request may block on another
  // request that blocks on the barrier).
  async blockedCount() {
    return Number(await sqlScalar(
      `WITH w AS (
         SELECT session_id, blocking_session_id FROM sys.dm_exec_requests WHERE blocking_session_id <> 0
       ), chain AS (
         SELECT session_id, blocking_session_id FROM w WHERE blocking_session_id = ${this.#spid}
         UNION ALL
         SELECT w.session_id, w.blocking_session_id FROM w JOIN chain ON w.blocking_session_id = chain.session_id
       )
       SELECT COUNT(DISTINCT session_id) FROM chain`,
      "blocked count",
    ));
  }

  async release() {
    if (!this.#child) return;
    this.#child.stdin.write("COMMIT;\nGO\nEXIT\n");
    this.#child.stdin.end();
    await new Promise((r) => this.#child.on("exit", r));
    this.#child = undefined;
  }
}

// ── Account setup through the API's own flows ─────────────────────────────────
const uniqueEmail = (tag) => `x08-${tag}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}@x08.probe.local`;

async function register(tag) {
  const email = uniqueEmail(tag);
  const mark = logMark();
  const start = await call("/api/v1/auth/registration/start", { method: "POST", body: { email, preferredLanguage: "en" } });
  // Tracked from here, not from the end: a run that dies after this call has
  // already left a pending registration and an outbox row behind, and the revert
  // finds them by the address.
  const account = { email, userId: null };
  accounts.push(account);
  if (start.status !== 200) throw new SetupError(`registration/start: ${start.status} ${start.text.slice(0, 160)}`);
  const otp = await otpAfter(mark);
  const pendingId = start.json.pendingId ?? start.json.PendingId;

  const verify = await call("/api/v1/auth/registration/verify", { method: "POST", body: { pendingId, otp } });
  if (verify.status !== 204) throw new SetupError(`registration/verify: ${verify.status} ${verify.text.slice(0, 160)}`);

  const complete = await call("/api/v1/auth/registration/complete", {
    method: "POST",
    body: { pendingId, otp, password: PASSWORD, firstName: "Probe", lastName: "User", createOrganization: false },
  });
  if (complete.status !== 200) throw new SetupError(`registration/complete: ${complete.status} ${complete.text.slice(0, 160)}`);

  const token = complete.json.token?.accessToken ?? complete.json.Token?.AccessToken;
  const refreshToken = complete.json.token?.refreshToken ?? complete.json.Token?.RefreshToken;
  const userId = complete.json.user?.id ?? complete.json.User?.Id;
  if (!token || !userId) throw new SetupError("registration/complete returned no token or user id");
  account.userId = userId;
  return { email, userId, token, refreshToken };
}

async function enableTwoFactor(token) {
  const setup = await call("/api/v1/auth/2fa/setup", { method: "POST", token });
  if (setup.status !== 200) throw new SetupError(`2fa/setup: ${setup.status} ${setup.text.slice(0, 160)}`);
  const secret = setup.json.secret ?? setup.json.Secret;

  const enable = await call("/api/v1/auth/2fa/enable", { method: "POST", token, body: { code: totpNow(secret) } });
  if (enable.status !== 200) throw new SetupError(`2fa/enable: ${enable.status} ${enable.text.slice(0, 160)}`);
  return { secret, recoveryCodes: enable.json.recoveryCodes ?? enable.json.RecoveryCodes };
}

async function challenge(email, { paced = false } = {}) {
  const login = await (paced ? callPaced : call)("/api/v1/auth/login", { method: "POST", body: { email, password: PASSWORD } });
  if (login.status !== 200) throw new SetupError(`login: ${login.status} ${login.text.slice(0, 160)}`);
  const challengeToken = login.json.twoFactorChallengeToken ?? login.json.TwoFactorChallengeToken;
  if (!challengeToken) throw new SetupError("login did not return a challenge token");
  return challengeToken;
}

// Warm the API's SQL pool and the client sockets so the race is not serialized
// by cold-connection setup.
async function warmUp(token) {
  await Promise.all(Array.from({ length: RACERS + 1 }, () => call("/api/v1/auth/sessions", { token })));
}

// Fire the racing requests together, then let them pile up against the barrier.
// Each blocks on its first write and stays blocked while the barrier holds, so a
// few milliseconds of TLS stagger does not stop them overlapping: the peak block
// count climbs to `need`. Release the barrier once it does, and report the peak.
async function race(requests, barrier, need) {
  // Every request holds its last body byte until the gate opens, so they all
  // finish model binding — and start their handlers — at the same instant.
  let open;
  const gate = new Promise((r) => (open = r));
  const pending = requests.map((r) => rawPost(r.path, r.body, gate, r.token));
  await sleep(600); // every socket connected and primed with all but its last byte
  open();
  let peak = 0;
  for (let attempt = 0; attempt < 60; attempt++) {
    await sleep(100);
    peak = Math.max(peak, await barrier.blockedCount());
    if (peak >= need) break;
  }
  await barrier.release();
  return { answers: await Promise.all(pending), peak };
}

const tally = (answers) => {
  const counts = {};
  for (const a of answers) counts[a.code] = (counts[a.code] ?? 0) + 1;
  return counts;
};
const show = (counts) => Object.entries(counts).map(([code, n]) => `${n}×${code}`).join(", ");
const anyServerError = (answers) => answers.some((a) => a.status >= 500 || a.status === 0);
const any429 = (answers) => answers.some((a) => a.status === 429);

function record(name, verdict, detail) {
  verdicts.push({ name, verdict });
  console.log(`${verdict.padEnd(12)} ${name.padEnd(26)} ${detail}`);
}

// ── Scenarios ─────────────────────────────────────────────────────────────────
async function burst() {
  const { email, userId, token } = await register("burst");
  const { secret } = await enableTwoFactor(token);
  const challengeToken = await challenge(email);
  await warmUp(token);
  await sqlScalar(
    `SELECT 1 WHERE (SELECT FailedAttempts FROM dbo.TwoFactorAuth WHERE UserId = '${userId}') = 0
       AND (SELECT AttemptCount FROM dbo.TwoFactorChallenges WHERE UserId = '${userId}' AND UsedAt IS NULL) = 0`,
    "burst precondition (counters at 0)",
  );

  const barrier = new Barrier();
  // Lock both rows a verify writes: the account row (A2, written first) and the
  // live challenge row (A1). Every request blocks on whichever it writes first.
  await barrier.hold(
    `SELECT FailedAttempts FROM dbo.TwoFactorAuth WITH (UPDLOCK, ROWLOCK) WHERE UserId = '${userId}';
     SELECT AttemptCount FROM dbo.TwoFactorChallenges WITH (UPDLOCK, ROWLOCK) WHERE UserId = '${userId}' AND UsedAt IS NULL;`,
  );

  const codes = wrongTotpCodes(secret, RACERS);
  const { answers, peak } = await race(
    codes.map((code) => ({ path: "/api/v1/auth/2fa/verify", body: { challengeToken, code } })),
    barrier,
    RACERS,
  );

  const counts = tally(answers);
  const invalid = counts["User.InvalidTwoFactorCode"] ?? 0;
  const locked = counts["TwoFactor.LockedOut"] ?? 0;
  const row = (await sql(`SELECT FailedAttempts, IIF(LockedUntil > SYSUTCDATETIME(), 1, 0) FROM dbo.TwoFactorAuth WHERE UserId = '${userId}'`)).split("|").map((s) => s.trim());
  const detail = `${show(counts)}; FailedAttempts=${row[0]}, locked=${row[1]}; overlap peak=${peak}/${RACERS}`;

  if (anyServerError(answers)) return record("burst", "FAIL", `server error — ${detail}`);
  if (any429(answers)) return record("burst", "INCONCLUSIVE", `429 (window not fresh) — ${detail}`);
  const unexpected = answers.filter((a) => !["User.InvalidTwoFactorCode", "TwoFactor.LockedOut", "TwoFactor.ChallengeInvalid"].includes(a.code));
  if (unexpected.length) return record("burst", "FAIL", `unexpected ${unexpected[0].code} — ${detail}`);
  if (invalid !== 5 || row[0] !== "5" || row[1] !== "1") return record("burst", "FAIL", `cap not held — ${detail}`);
  if (peak < RACERS || locked !== 10) return record("burst", "INCONCLUSIVE", `overlap not proven (need ${RACERS} blocked and 10 LockedOut) — ${detail}`);
  record("burst", "PASS", detail);
}

async function recovery() {
  const { email, userId, token } = await register("recovery");
  const { recoveryCodes } = await enableTwoFactor(token);
  const code = recoveryCodes[0];
  const first = await challenge(email);
  const second = await challenge(email); // supersedes the first
  await warmUp(token);

  // Reopen the first challenge so one code races on two live challenges.
  await sql(`UPDATE dbo.TwoFactorChallenges SET UsedAt = NULL WHERE UserId = '${userId}'`);
  const live = await sqlScalar(`SELECT COUNT(*) FROM dbo.TwoFactorChallenges WHERE UserId = '${userId}' AND UsedAt IS NULL`, "recovery reopen");
  if (live !== "2") return record("recovery", "INCONCLUSIVE", `expected 2 live challenges after reopen, found ${live}`);

  const barrier = new Barrier();
  // Both commits settle the account row; locking it makes both read the same
  // recovery set, then queue at the settle. The loser's set no longer matches.
  await barrier.hold(`SELECT FailedAttempts FROM dbo.TwoFactorAuth WITH (UPDLOCK, ROWLOCK) WHERE UserId = '${userId}';`);

  const { answers, peak } = await race(
    [first, second].map((ct) => ({ path: "/api/v1/auth/2fa/verify", body: { challengeToken: ct, code, useRecoveryCode: true } })),
    barrier,
    2,
  );

  const ok = answers.filter((a) => a.status === 200).length;
  const loser = answers.find((a) => a.status !== 200)?.code ?? "(none)";
  const remaining = await sql(`SELECT (LEN(RecoveryCodes) - LEN(REPLACE(RecoveryCodes, '$argon2id$', ''))) / LEN('$argon2id$') FROM dbo.TwoFactorAuth WHERE UserId = '${userId}' AND RecoveryCodes IS NOT NULL`);
  const detail = `200×${ok}, loser=${loser}, remaining=${remaining}; overlap peak=${peak}/2`;

  if (anyServerError(answers)) return record("recovery", "FAIL", `server error — ${detail}`);
  if (any429(answers)) return record("recovery", "INCONCLUSIVE", `429 (window not fresh) — ${detail}`);
  if (ok !== 1 || remaining !== "9") return record("recovery", "FAIL", `code not spent exactly once — ${detail}`);
  if (peak < 2 || loser !== "TwoFactor.ChallengeInvalid") return record("recovery", "INCONCLUSIVE", `overlap not proven (loser should be ChallengeInvalid from the lost commit) — ${detail}`);
  record("recovery", "PASS", detail);
}

async function verifyEmail() {
  const { email, userId, token } = await register("vmail");
  await sql(`UPDATE dbo.Users SET IsEmailConfirmed = 0 WHERE Id = '${userId}'`);
  const resend = await call("/api/v1/auth/resend-verification-email", { method: "POST", body: { email } });
  if (resend.status !== 200) throw new SetupError(`resend-verification-email: ${resend.status} ${resend.text.slice(0, 160)}`);
  await warmUp(token);
  const tokenId = await sqlScalar(`SELECT TOP 1 CONVERT(varchar(36), Id) FROM dbo.EmailVerificationTokens WHERE UserId = '${userId}' AND UsedAt IS NULL ORDER BY CreatedAt DESC`, "verify-email token row");

  const barrier = new Barrier();
  await barrier.hold(`SELECT AttemptCount FROM dbo.EmailVerificationTokens WITH (UPDLOCK, ROWLOCK) WHERE Id = '${tokenId}';`);

  const codes = codesExcept(new Set(), RACERS); // the OTP is random; any 15 distinct 6-digit codes are wrong
  const { answers, peak } = await race(
    codes.map((otp) => ({ path: "/api/v1/auth/verify-email", body: { email, otp } })),
    barrier,
    RACERS,
  );

  const counts = tally(answers);
  const invalid = counts["EmailVerification.InvalidOrExpiredOtp"] ?? 0;
  const tooMany = counts["EmailVerification.TooManyAttempts"] ?? 0;
  const row = (await sql(`SELECT t.AttemptCount, u.IsEmailConfirmed, IIF(t.UsedAt IS NULL, 1, 0)
                          FROM dbo.EmailVerificationTokens t JOIN dbo.Users u ON u.Id = t.UserId
                          WHERE t.Id = '${tokenId}'`)).split("|").map((s) => s.trim());
  const detail = `${show(counts)}; AttemptCount=${row[0]}, confirmed=${row[1]}, unused=${row[2]}; overlap peak=${peak}/${RACERS}`;

  if (anyServerError(answers)) return record("verify-email", "FAIL", `server error — ${detail}`);
  if (any429(answers)) return record("verify-email", "INCONCLUSIVE", `429 (window not fresh) — ${detail}`);
  if (answers.some((a) => a.status === 200)) return record("verify-email", "FAIL", `a wrong code confirmed the address — ${detail}`);
  const unexpected = answers.filter((a) => !["EmailVerification.InvalidOrExpiredOtp", "EmailVerification.TooManyAttempts"].includes(a.code));
  if (unexpected.length) return record("verify-email", "FAIL", `unexpected ${unexpected[0].code} — ${detail}`);
  if (row[0] !== "5" || row[1] !== "0" || row[2] !== "1") return record("verify-email", "FAIL", `cap or outcome wrong — ${detail}`);
  if (peak < RACERS || tooMany > 0 || invalid <= 5) return record("verify-email", "INCONCLUSIVE", `overlap not proven (need ${RACERS} blocked, 0 TooManyAttempts, >5 InvalidOrExpiredOtp) — ${detail}`);
  record("verify-email", "PASS", detail);
}

// ── totp-replay (X01): one correct authenticator-app code counts once ─────────
const REUSED = "TwoFactor.CodeAlreadyUsed";
const answerOf = (r) => (r.status === 200 ? "200" : r.json?.code ?? `HTTP ${r.status}`);

async function verifyPaced(challengeToken, code) {
  return answerOf(await callPaced("/api/v1/auth/2fa/verify", { method: "POST", body: { challengeToken, code } }));
}

// The step the account last accepted, as the database holds it (NULL before X01).
const storedStep = (userId) =>
  sql(`SELECT ISNULL(CONVERT(varchar(20), LastUsedTimeStep), 'NULL') FROM dbo.TwoFactorAuth WHERE UserId = '${userId}'`);

// A sequential part: the first code signs in, the second must be refused as a reuse.
// `waitsBefore` is pacedWaits when the part began: a rate-limit wait inside the
// part can push a precomputed code out of the window, so an unexpected answer
// after one is INCONCLUSIVE, never FAIL. A reused code that signs in is a FAIL
// whatever happened before it.
function judgeSequential(name, first, second, detail, waitsBefore) {
  const waited = pacedWaits > waitsBefore;
  if ([first, second].some((a) => /^HTTP 5|^HTTP 0/.test(a))) return record(name, "FAIL", `server error — ${detail}`);
  if ([first, second].includes("Http.RateLimited")) return record(name, "INCONCLUSIVE", `429 after one wait — ${detail}`);
  if (first !== "200") return record(name, "INCONCLUSIVE", `the fresh code did not sign in — ${detail}`);
  if (second === "200") return record(name, "FAIL", `the reused code signed in again — ${detail}`);
  if (second !== REUSED && waited) return record(name, "INCONCLUSIVE", `unexpected ${second} after a rate-limit wait moved the clock — ${detail}`);
  if (second !== REUSED) return record(name, "FAIL", `unexpected ${second} — ${detail}`);
  record(name, "PASS", detail);
}

const settingsRevert = [
  "POST /api/v1/admin/system-settings/TwoFactor/reset (as an account with system-settings:manage), or:",
  "DELETE FROM dbo.SystemSettingsOverrides WHERE SectionKey = N'TwoFactor'; -- then restart the API, or wait up to 5 minutes for its refresh",
].join("\n");

async function totpReplay() {
  const { email, userId, token } = await register("replay");
  const { secret } = await enableTwoFactor(token); // since X01 this claims the enabling code's step

  // (a) A fresh code signs in; the same code on a NEW challenge is a reuse.
  let waitsBefore = pacedWaits;
  const a = step() + 1;
  const aFirst = await verifyPaced(await challenge(email, { paced: true }), codeAt(secret, a));
  const aStored = await storedStep(userId);
  const aAgain = await verifyPaced(await challenge(email, { paced: true }), codeAt(secret, a));
  judgeSequential("totp-replay (a) sequential", aFirst, aAgain, `first=${aFirst}, same code again=${aAgain}; stored step=${aStored} (code step ${a})`, waitsBefore);

  // (c) The code of step s after the code of step s+1: older than the last one
  // accepted, so refused. Both steps are fresh, so the refusal is the order alone.
  const c = await freshStepAfter(a);
  waitsBefore = pacedWaits;
  const cNewer = await verifyPaced(await challenge(email, { paced: true }), codeAt(secret, c));
  const cOlder = await verifyPaced(await challenge(email, { paced: true }), codeAt(secret, c - 1));
  judgeSequential("totp-replay (c) older after newer", cNewer, cOlder, `step ${c}=${cNewer}, then step ${c - 1}=${cOlder}; stored step=${await storedStep(userId)}`, waitsBefore);

  // (b) Two live challenges, one fresh code on both at the same instant.
  const b = await freshStepAfter(c);
  const first = await challenge(email, { paced: true });
  const second = await challenge(email, { paced: true }); // supersedes the first
  await warmUp(token);
  // Reopen the first, as the recovery scenario does: a sign-in supersedes the one
  // before it, and the race needs both live. Only these two: every earlier
  // challenge of this account stays spent.
  await sql(`UPDATE dbo.TwoFactorChallenges SET UsedAt = NULL
             WHERE Id IN (SELECT TOP 2 Id FROM dbo.TwoFactorChallenges WHERE UserId = '${userId}' ORDER BY CreatedAt DESC)`);
  const live = await sqlScalar(`SELECT COUNT(*) FROM dbo.TwoFactorChallenges WHERE UserId = '${userId}' AND UsedAt IS NULL`, "totp-replay reopen");
  if (live !== "2") {
    record("totp-replay (b) concurrent", "INCONCLUSIVE", `expected 2 live challenges after reopen, found ${live}`);
  } else {
    const barrier = new Barrier();
    // Both requests' first write is the attempt reservation on the account row:
    // they queue there, so the peak proves both were in flight together. Their
    // claims come later and may run one after the other — what makes the claim
    // safe even then is that it is one conditional statement, which the SQL
    // guard pins; this part proves the outcome a user would see.
    await barrier.hold(`SELECT FailedAttempts FROM dbo.TwoFactorAuth WITH (UPDLOCK, ROWLOCK) WHERE UserId = '${userId}';`);
    const { answers, peak } = await race(
      [first, second].map((ct) => ({ path: "/api/v1/auth/2fa/verify", body: { challengeToken: ct, code: codeAt(secret, b) } })),
      barrier,
      2,
    );
    const ok = answers.filter((x) => x.code === "200").length;
    const reused = answers.filter((x) => x.code === REUSED).length;
    const detail = `${show(tally(answers))}; stored step=${await storedStep(userId)} (code step ${b}); in flight together (held at the reservation) peak=${peak}/2`;
    if (anyServerError(answers)) record("totp-replay (b) concurrent", "FAIL", `server error — ${detail}`);
    else if (any429(answers)) record("totp-replay (b) concurrent", "INCONCLUSIVE", `429 (window not fresh) — ${detail}`);
    else if (ok > 1) record("totp-replay (b) concurrent", "FAIL", `one code signed in ${ok} times — ${detail}`);
    else if (ok !== 1 || reused !== 1) record("totp-replay (b) concurrent", "FAIL", `expected one 200 and one ${REUSED} — ${detail}`);
    else if (peak < 2) record("totp-replay (b) concurrent", "INCONCLUSIVE", `not proven in flight together — ${detail}`);
    else record("totp-replay (b) concurrent", "PASS", detail);
  }

  // (d) The rollout switch off, through the System Settings API: the same code
  // signs in twice and the accepted reuse is logged — without a restart.
  if (!ADMIN.email || !ADMIN.password) {
    record("totp-replay (d) switch off", "INCONCLUSIVE", "set PROBE_ADMIN_EMAIL and PROBE_ADMIN_PASSWORD (an account with system-settings:manage)");
    return;
  }
  const admin = await callPaced("/api/v1/auth/login", { method: "POST", body: ADMIN });
  const adminToken = admin.json?.token?.accessToken;
  if (admin.status !== 200 || !adminToken) {
    record("totp-replay (d) switch off", "INCONCLUSIVE", `the admin sign-in did not return a token (${admin.status}; a second factor on that account?)`);
    return;
  }
  const readSection = async () =>
    (await call("/api/v1/admin/system-settings", { token: adminToken })).json?.sections?.find((s) => s.key === "TwoFactor");
  const before = await readSection();
  if (!before) {
    record("totp-replay (d) switch off", "FAIL", "the API has no TwoFactor settings section");
    return;
  }
  // What to put back afterwards: an override the operator already had, or none.
  const beforeField = before.fields?.find((f) => f.path === "RejectReusedCodes");
  const priorOverride = beforeField?.source === "database" ? beforeField.effectiveValue : undefined;
  console.log(
    `--- totp-replay (d) switches TwoFactor:RejectReusedCodes off; to undo by hand:\n${settingsRevert}` +
      (priorOverride === undefined
        ? ""
        : `\n(the section already had the override RejectReusedCodes=${priorOverride}: save that value again instead of resetting)`),
  );
  const put = await call("/api/v1/admin/system-settings/TwoFactor", {
    method: "PUT",
    token: adminToken,
    body: { overrides: { RejectReusedCodes: false }, rowVersion: before.rowVersion ?? null },
  });
  if (put.status !== 200) {
    // Nothing was changed, so there is nothing to undo.
    record("totp-replay (d) switch off", "INCONCLUSIVE", `the switch could not be saved (PUT ${put.status}: ${put.text.slice(0, 160)})`);
    return;
  }
  try {
    const effective = (await readSection())?.fields?.find((f) => f.path === "RejectReusedCodes")?.effectiveValue;
    if (effective !== false) {
      record("totp-replay (d) switch off", "INCONCLUSIVE", `the switch did not take (effective=${effective}); is AUTH_DISABLE_DB_SETTINGS set?`);
      return;
    }
    const d = await freshStepAfter(b);
    const mark = logMark();
    const dFirst = await verifyPaced(await challenge(email, { paced: true }), codeAt(secret, d));
    const dAgain = await verifyPaced(await challenge(email, { paced: true }), codeAt(secret, d));
    await sleep(500); // let the file sink flush the line
    const appended = mark.file ? readFileSync(mark.file, "utf8").slice(mark.length) : "";
    const logged = appended.split(/\r?\n/).some((line) =>
      line.includes("Reused two-factor code accepted (RejectReusedCodes=false)") && line.includes(userId));
    const detail = `first=${dFirst}, same code again=${dAgain}; accepted-reuse line logged=${logged}`;
    if (dFirst !== "200") record("totp-replay (d) switch off", "INCONCLUSIVE", `the fresh code did not sign in — ${detail}`);
    else if (dAgain === REUSED) record("totp-replay (d) switch off", "FAIL", `the switch was ignored: still refused without a restart — ${detail}`);
    else if (dAgain !== "200" || !logged) record("totp-replay (d) switch off", "FAIL", detail);
    else record("totp-replay (d) switch off", "PASS", detail);
  } finally {
    // Put back exactly what was there: the operator's own override if there was
    // one, else no override at all (the reset falls back to the files).
    const current = await readSection();
    const restore = priorOverride === undefined
      ? await call("/api/v1/admin/system-settings/TwoFactor/reset", { method: "POST", token: adminToken })
      : await call("/api/v1/admin/system-settings/TwoFactor", {
          method: "PUT",
          token: adminToken,
          body: { overrides: { RejectReusedCodes: priorOverride }, rowVersion: current?.rowVersion ?? null },
        });
    const after = (await readSection())?.fields?.find((f) => f.path === "RejectReusedCodes");
    const expected = priorOverride ?? beforeField?.effectiveValue;
    const ok = restore.status === 200 && after?.effectiveValue === expected;
    console.log(`--- TwoFactor:RejectReusedCodes restored (${restore.status}); effective now ${after?.effectiveValue}, was ${beforeField?.effectiveValue}${ok ? "" : ` — UNDO BY HAND:\n${settingsRevert}`}`);
    // A switch left off weakens the dev API for whatever runs next: the run fails
    // until it is put back, rather than reporting the part's own verdict alone.
    if (!ok) {
      record("totp-replay (d) restore", "FAIL", `the switch was not restored (status ${restore.status}, effective ${after?.effectiveValue}, expected ${expected}) — undo by hand`);
    }
  }
}

// ── lifecycle (X02): switching the second factor off and on ───────────────────
const TWO_FACTOR_CHANGED = "two-factor-changed";

// The two-factor-changed notices written for the account, in the outbox.
const noticesFor = (userId) =>
  sql(`SELECT COUNT(*) FROM dbo.NotificationOutbox WHERE RecipientUserId = '${userId}' AND NotificationTypeCode = N'${TWO_FACTOR_CHANGED}'`);

async function renew(refreshToken) {
  return answerOf(await callPaced("/api/v1/auth/refresh", { method: "POST", body: { refreshToken } }));
}

async function lifecycleDisable() {
  const name = "lifecycle-disable";
  // The first device: the registration's own session, so its sign-in is recent
  // enough to change two-factor.
  const { email, userId, token, refreshToken } = await register("ldisable");
  if (!refreshToken) throw new SetupError("registration/complete returned no refresh token in its body");
  const { secret } = await enableTwoFactor(token);

  // A second device signs in with the password and a code.
  const signedInAt = step() + 1;
  const signIn = await callPaced("/api/v1/auth/2fa/verify", {
    method: "POST",
    body: { challengeToken: await challenge(email, { paced: true }), code: codeAt(secret, signedInAt) },
  });
  const otherRefresh = signIn.json?.token?.refreshToken;
  if (signIn.status !== 200 || !otherRefresh) {
    throw new SetupError(`the second device did not sign in: ${signIn.status} ${signIn.text.slice(0, 160)}`);
  }

  // The first device switches two-factor off with a code newer than the one the
  // second device used, so the refusal of a reused code cannot get in the way.
  const offAt = await freshStepAfter(signedInAt);
  const disable = await callPaced("/api/v1/auth/2fa/disable", {
    method: "POST",
    token,
    body: { code: codeAt(secret, offAt), useRecoveryCode: false },
  });
  const otherAfter = await renew(otherRefresh);
  const ownAfter = await renew(refreshToken);
  await sleep(500); // the notice is written by an event handler after the response
  const notices = await noticesFor(userId);
  const state = (await sql(
    `SELECT u.IsTwoFactorEnabled, (SELECT COUNT(*) FROM dbo.TwoFactorAuth t WHERE t.UserId = u.Id) FROM dbo.Users u WHERE u.Id = '${userId}'`,
  )).split("|").map((s) => s.trim());
  const detail = `disable=${disable.status} ${answerOf(disable)}, other device renews=${otherAfter}, this device renews=${ownAfter}; ` +
    `${TWO_FACTOR_CHANGED} notices=${notices}; flag=${state[0]}, factor rows=${state[1]}`;

  if (disable.status >= 500 || [otherAfter, ownAfter].some((a) => /^HTTP 5|^HTTP 0/.test(a))) return record(name, "FAIL", `server error — ${detail}`);
  if ([disable.status === 429, otherAfter === "Http.RateLimited", ownAfter === "Http.RateLimited"].some(Boolean)) return record(name, "INCONCLUSIVE", `429 after one wait — ${detail}`);
  if (disable.status !== 204) return record(name, "FAIL", `two-factor was not switched off — ${detail}`);
  if (otherAfter === "200") return record(name, "FAIL", `the other device kept its session after two-factor was switched off — ${detail}`);
  if (otherAfter !== "Auth.RefreshTokenRevoked") return record(name, "FAIL", `unexpected answer to the other device — ${detail}`);
  if (ownAfter !== "200") return record(name, "FAIL", `the device that switched it off was signed out too — ${detail}`);
  if (state[0] !== "0" || state[1] !== "0") return record(name, "FAIL", `the flag or the factor row survived — ${detail}`);
  if (notices !== "2") return record(name, "FAIL", `expected the "enabled" and "disabled" notices (is seed 0021 on this database?) — ${detail}`);
  record(name, "PASS", detail);
}

async function lifecycleEnable() {
  const name = "lifecycle-enable";
  const enablers = 10;
  const { email, userId, token } = await register("lenable");
  const setup = await call("/api/v1/auth/2fa/setup", { method: "POST", token });
  if (setup.status !== 200) throw new SetupError(`2fa/setup: ${setup.status} ${setup.text.slice(0, 160)}`);
  const secret = setup.json.secret ?? setup.json.Secret;
  await warmUp(token);

  // Every enable's first write is the attempt reservation on the factor row (the
  // build before X02 writes the whole row instead): lock it so all ten queue there
  // together before any of them gets further.
  const barrier = new Barrier();
  await barrier.hold(`SELECT FailedAttempts FROM dbo.TwoFactorAuth WITH (UPDLOCK, ROWLOCK) WHERE UserId = '${userId}';`);
  const code = codeAt(secret, step() + 1);
  const { answers, peak } = await race(
    Array.from({ length: enablers }, () => ({ path: "/api/v1/auth/2fa/enable", body: { code }, token })),
    barrier,
    enablers,
  );

  const winners = answers.filter((a) => a.code === "200");
  const state = (await sql(
    `SELECT u.IsTwoFactorEnabled, ISNULL(CONVERT(varchar(1), t.IsEnabled), '-') FROM dbo.Users u LEFT JOIN dbo.TwoFactorAuth t ON t.UserId = u.Id WHERE u.Id = '${userId}'`,
  )).split("|").map((s) => s.trim());

  // The codes the winner was shown must be the ones stored.
  let signsIn = "(not tried)";
  const shown = winners.length === 1 ? winners[0].json?.recoveryCodes ?? winners[0].json?.RecoveryCodes : null;
  if (shown?.length) {
    signsIn = answerOf(await callPaced("/api/v1/auth/2fa/verify", {
      method: "POST",
      body: { challengeToken: await challenge(email, { paced: true }), code: shown[0], useRecoveryCode: true },
    }));
  }
  const detail = `${show(tally(answers))}; flag=${state[0]}, factor enabled=${state[1]}; the winner's recovery code signs in=${signsIn}; overlap peak=${peak}/${enablers}`;

  if (anyServerError(answers)) return record(name, "FAIL", `server error — ${detail}`);
  if (any429(answers)) return record(name, "INCONCLUSIVE", `429 (window not fresh) — ${detail}`);
  if (winners.length > 1) return record(name, "FAIL", `${winners.length} enables succeeded, each showing codes of its own — ${detail}`);
  if (winners.length === 0) return record(name, "FAIL", `no enable succeeded with a correct code — ${detail}`);
  const unexpected = answers.filter((a) => !["200", "User.TwoFactorAlreadyEnabled", "TwoFactor.LockedOut"].includes(a.code));
  if (unexpected.length) return record(name, "FAIL", `unexpected ${unexpected[0].code} — ${detail}`);
  if (state[0] !== "1" || state[1] !== "1") return record(name, "FAIL", `the factor and the account flag disagree — ${detail}`);
  if (signsIn !== "200") return record(name, "FAIL", `the codes shown do not sign in — ${detail}`);
  if (peak < enablers) return record(name, "INCONCLUSIVE", `overlap not proven (need ${enablers} blocked) — ${detail}`);
  record(name, "PASS", detail);
}

// ── Revert SQL. A bare DELETE FROM dbo.Users hits non-cascading foreign keys
//    (error 547), so remove the child rows first, in the order HardDeleteAsync
//    uses, inside one transaction per account. ────────────────────────────────
function revertBlock({ email }) {
  // Every table with a non-cascading foreign key to Users, so the Users delete
  // does not hit error 547. LoginAttempts and AuditLogs are audit rows the real
  // hard-delete only anonymises; a throwaway probe account removes them outright
  // (AuditLogs has no foreign key, so it is cleared by subject and by actor).
  const byUser = [
    "UserEncryptionKeys", "RefreshTokens", "UserSessions", "IdpSessions", "AuthorizationCodes",
    "UserExternalLogins", "EmailVerificationTokens", "PasswordResetTokens", "AccountDeletionVerifications",
    "PasswordHistory", "TwoFactorChallenges", "TwoFactorAuth", "UserRoles", "UserPermissions",
    "ApplicationUserAccess", "OrganizationUserRoles", "OrganizationUserPermissions", "OrganizationUsers",
    "UserUiPreferences", "UserKnownDevices", "LoginAttempts",
  ];
  const lines = [
    "SET XACT_ABORT ON;", "BEGIN TRAN;",
    // The account is found by its address, so a run that died before the
    // registration completed is reverted too: its pending registration and its
    // outbox rows go, and every delete keyed on the user matches nothing.
    `DECLARE @UserId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Users WHERE LOWER(Email) = LOWER('${email}'));`,
    "DELETE FROM dbo.SecretOperationChallenges WHERE RequestedBy = @UserId;",
    "DELETE FROM dbo.OwnershipTransferCodes WHERE TargetUserId = @UserId OR InitiatedBy = @UserId;",
    `DELETE FROM dbo.OrganizationInvitations WHERE InvitedBy = @UserId OR AcceptedByUserId = @UserId OR LOWER(Email) = LOWER('${email}');`,
    `DELETE FROM dbo.NotificationOutbox WHERE RecipientUserId = @UserId OR Recipient = '${email}';`,
    ...byUser.map((t) => `DELETE FROM dbo.${t} WHERE UserId = @UserId;`),
    "DELETE FROM dbo.AuditLogs WHERE UserId = @UserId OR PerformedBy = @UserId;",
    `DELETE FROM dbo.PendingRegistrations WHERE LOWER(Email) = LOWER('${email}');`,
    "DELETE FROM dbo.Users WHERE Id = @UserId;",
    "COMMIT;",
  ];
  return lines.join("\n");
}

async function cleanup() {
  // Run the revert for every account this process created, then print the same
  // blocks so the owner can re-run them if needed.
  for (const account of accounts) {
    try {
      await sql(revertBlock(account));
    } catch (error) {
      const detail = `${error.stdout ?? ""}${error.stderr ?? ""}`.trim() || error.message;
      console.error(`cleanup failed for ${account.email}: ${detail}`);
    }
  }
  if (accounts.length) {
    console.log("\n--- revert SQL (already run by this probe; re-run to undo any row it left) ---");
    // GO after each block: every block declares @UserId, so pasted as one batch
    // the second declaration would fail and nothing would run.
    for (const account of accounts) console.log(`-- ${account.email}\n${revertBlock(account)}\nGO`);
  }
}

// ── Driver ───────────────────────────────────────────────────────────────────
const scenarios = {
  burst,
  recovery,
  "verify-email": verifyEmail,
  "totp-replay": totpReplay,
  "lifecycle-disable": lifecycleDisable,
  "lifecycle-enable": lifecycleEnable,
};
// `all` is every scenario that runs against an API started with
// AUTH_DISABLE_DB_SETTINGS=true. totp-replay needs it unset (its part (d) saves
// a DB-backed setting), so it is run on its own, never folded in here.
const allScenarios = Object.keys(scenarios).filter((name) => name !== "totp-replay");
const requested = process.argv[2];

async function main() {
  const names = requested === "all" ? allScenarios : [requested];
  if (!names.every((n) => scenarios[n])) {
    console.error(`Usage: node Tools/probes/two-factor-race.mjs <${Object.keys(scenarios).join("|")}|all>`);
    console.error(`  all = ${allScenarios.join(", ")} (API run with AUTH_DISABLE_DB_SETTINGS=true); run totp-replay on its own, without it.`);
    process.exit(2);
  }

  for (const [index, name] of names.entries()) {
    if (index > 0) { console.log(`--- waiting 61 s for a fresh rate window before ${name} ---`); await sleep(61_000); }
    console.log(`=== ${name} ===`);
    await scenarios[name]();
  }

  await cleanup();

  const fail = verdicts.filter((v) => v.verdict === "FAIL").length;
  const inconclusive = verdicts.filter((v) => v.verdict === "INCONCLUSIVE").length;
  console.log(`\n${fail ? `${fail} FAIL` : inconclusive ? `${inconclusive} INCONCLUSIVE` : "ALL PASS"}`);
  process.exit(fail ? 1 : inconclusive ? 4 : 0);
}

main().catch(async (error) => {
  const kind = error instanceof SetupError ? "setup aborted" : "probe aborted";
  console.error(`\n${kind}: ${error.message}`);
  await cleanup();
  process.exit(3);
});
