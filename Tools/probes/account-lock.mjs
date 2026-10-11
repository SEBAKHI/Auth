/**
 * Live proof of the X12 password lock on SQL Server, against a running dev API and
 * the dev database. The xUnit suite pins the statements' text
 * (RecordFailedLoginCadenceSqlTests, SuccessRepairSqlTests, LockSqlTextGuardTests);
 * this shows what SQL Server and the real handlers do with them. A check that
 * cannot fail proves nothing: run it against the build of origin/main before X12
 * too, and every scenario must FAIL there.
 *
 * Run the API with small limits so a scenario takes minutes, not hours:
 *   Password__ConsecutiveFailureLimit=10  Password__MaxFailedAttempts=5
 *   Password__LockoutDurationMinutes=1    AUTH_DISABLE_DB_SETTINGS=true
 * (Email off, the dev default: the probe reads one-time codes and the reset link
 * from the API's Serilog file.) Addresses are simulated with X-Forwarded-For,
 * which the API trusts before S15: each address has its own rate window and its
 * own per-address ceiling.
 *
 * Scenarios (positional argument):
 *   cadence   RecordFailedLoginCadence + SuccessRepair on SQL Server, from a
 *             stranger's address S and the owner's own address F:
 *             5 wrong from S lock for the duration (Status 3, count 5, an expiry);
 *             a 6th is refused AccountLockedUntil and NOT counted; after the lapse
 *             the next wrong repairs the row and the count is KEPT (6); 4 more make
 *             10: the hard lock (Status 3, count 10, no expiry); S with the right
 *             password is told AccountLockedResetRequired, and still is after the
 *             duration passes again; the owner's refresh token still renews (200);
 *             F with the right password signs in and the row is clear (1, 0, none).
 *             The build before X12 FAILS at "count kept" (the lapse wiped it).
 *   exits     LockExitsDevRun: the hard lock set on the row, then lifted by each
 *             exit in turn, the row read after each: change-password (with the
 *             session's token), a completed password reset (the link read from the
 *             log), the familiar address, and an administrator's Unlock. Then an
 *             administrator's Deactivate refuses the next sign-in AccountInactive,
 *             and Activate clears it. The administrator parts need PROBE_ADMIN_EMAIL
 *             and PROBE_ADMIN_PASSWORD (users:manage, no second factor), else
 *             INCONCLUSIVE. The build before X12 FAILS the reset exit (it cleared
 *             only a timed lock) and the familiar exit (it read the hard lock as an
 *             administrator's).
 *   pending-deletion  PendingDeletionRecoverDevRun: an account pending deletion
 *             (public request + confirm); 5 wrong passwords on deletion/recover
 *             from address A; a 6th from A is refused InvalidCredentials and
 *             recorded "Source locked" (the ceiling, before the hash); address B
 *             recovers with the right password (200); the row was never locked
 *             (Status 1, count 0). The build before X12 FAILS: recover counted
 *             nothing and read the socket address, not the client's.
 *   all       the three, 61 s apart.
 *
 * Verdicts: PASS, FAIL (wrong outcome, a 5xx or an unexpected code), INCONCLUSIVE
 * (a precondition the probe cannot meet). Anything but PASS blocks the merge.
 *
 * Safety: a localhost API and a local SQL Server only; throwaway accounts made
 * through the API's own flows (the only direct writes are the lock columns of the
 * probe's own account, for the exits scenario, and the revert); no secret, code or
 * password printed; revert SQL printed at the end.
 *
 * Env: PROBE_API_URL (default https://localhost:5101), PROBE_LOGS_DIR (default
 * Auth/Auth_API/Logs), PROBE_SQLCMD_SRV (default localhost\SQLEXPRESS01), PROBE_SQLCMD_DB
 * (default AuthSystem), PROBE_ADMIN_EMAIL / PROBE_ADMIN_PASSWORD (exits only),
 * PROBE_LOCK_MINUTES (default 1: the API's Password:LockoutDurationMinutes),
 * PROBE_HARD (default 10) and PROBE_MAX (default 5): the API's limits.
 *
 * Usage: node Tools/probes/account-lock.mjs <cadence|exits|pending-deletion|all>
 * Exit:  0 all PASS · 1 a FAIL · 4 an INCONCLUSIVE (no FAIL) · 3 setup aborted · 2 refused
 */
import { execFile } from "node:child_process";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const HERE = dirname(fileURLToPath(import.meta.url));
const API = process.env.PROBE_API_URL ?? "https://localhost:5101";
const LOGS = process.env.PROBE_LOGS_DIR ?? resolve(HERE, "..", "..", "Auth", "Auth_API", "Logs");
const SERVER = process.env.PROBE_SQLCMD_SRV ?? "localhost\\SQLEXPRESS01";
const DB = process.env.PROBE_SQLCMD_DB ?? "AuthSystem";
const LOCK_MINUTES = Number(process.env.PROBE_LOCK_MINUTES ?? 1);
const HARD = Number(process.env.PROBE_HARD ?? 10);
const MAX = Number(process.env.PROBE_MAX ?? 5);
const ADMIN = { email: process.env.PROBE_ADMIN_EMAIL, password: process.env.PROBE_ADMIN_PASSWORD };
const PASSWORD = `Pr0be-${Math.random().toString(36).slice(2, 10)}!Zq`;
const OTHER_PASSWORD = `Pr0be-${Math.random().toString(36).slice(2, 10)}!Yw`;
const WRONG = "Wrong-Pr0be-1!";

// Documentation addresses (RFC 5737): never a real client.
const FAMILIAR = "198.51.100.10";
const STRANGER = "203.0.113.50";

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
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

class SetupError extends Error {}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const accounts = [];
const verdicts = [];

// ── HTTP ─────────────────────────────────────────────────────────────────────
async function call(path, { method = "GET", body, token, from } = {}) {
  const headers = { "content-type": "application/json" };
  if (token) headers.authorization = `Bearer ${token}`;
  if (from) headers["x-forwarded-for"] = from;
  const response = await fetch(`${API}${path}`, {
    method, headers, body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON body */ }
  return { status: response.status, json, text, code: json?.code ?? null };
}

// One wait on a 429: each address has its own "login" window.
async function paced(path, options) {
  const first = await call(path, options);
  if (first.status !== 429) return first;
  await sleep(61_000);
  return call(path, options);
}

const login = (email, password, from) =>
  paced("/api/v1/auth/login", { method: "POST", body: { email, password }, from });

// ── Serilog: one-time codes and the reset link the API logs with Email off ──
function logMark() {
  const files = readdirSync(LOGS).filter((f) => f.endsWith(".log")).map((f) => join(LOGS, f));
  if (files.length === 0) return { file: null, length: 0 };
  const file = files.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
  return { file, length: statSync(file).size };
}

async function fromLogAfter(mark, pattern, what) {
  for (let attempt = 0; attempt < 40; attempt++) {
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

const otpAfter = (mark) => fromLogAfter(mark, /OTP for "?[^\s":]+"?:\s*"?(\d{6})"?/g, "one-time code");
const resetTokenAfter = (mark) =>
  fromLogAfter(mark, /Password reset link for [^\s:]+:\s*\S*?[?&]token=([^&\s"]+)/g, "reset link").then(decodeURIComponent);

// ── sqlcmd (-b: a T-SQL error is a non-zero exit) ───────────────────────────
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

/** The lock columns of one row: { status, count, end (null or "future"/"past") }. */
async function lockRow(userId) {
  const raw = await sql(
    `SELECT CONCAT(Status, '|', FailedLoginAttempts, '|',
       CASE WHEN LockoutEndUtc IS NULL THEN 'none' WHEN LockoutEndUtc > SYSUTCDATETIME() THEN 'future' ELSE 'past' END)
     FROM dbo.Users WHERE Id = '${userId}'`);
  const [status, count, end] = raw.split("|");
  if (end === undefined) throw new SetupError(`no row for ${userId}`);
  return { status: Number(status), count: Number(count), end };
}

const show = (row) => `(Status ${row.status}, count ${row.count}, expiry ${row.end})`;

// ── Accounts through the API's own flows ─────────────────────────────────────
const uniqueEmail = (tag) => `x12-${tag}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}@x12.probe.local`;

async function register(tag, from) {
  const email = uniqueEmail(tag);
  const account = { email, userId: null };
  accounts.push(account);
  const mark = logMark();
  const start = await call("/api/v1/auth/registration/start", { method: "POST", body: { email, preferredLanguage: "en" }, from });
  if (start.status !== 200) throw new SetupError(`registration/start: ${start.status} ${start.text.slice(0, 160)}`);
  const otp = await otpAfter(mark);
  const pendingId = start.json.pendingId ?? start.json.PendingId;
  const verify = await call("/api/v1/auth/registration/verify", { method: "POST", body: { pendingId, otp }, from });
  if (verify.status !== 204) throw new SetupError(`registration/verify: ${verify.status} ${verify.text.slice(0, 160)}`);
  const complete = await call("/api/v1/auth/registration/complete", {
    method: "POST", from,
    body: { pendingId, otp, password: PASSWORD, firstName: "Probe", lastName: "User", createOrganization: false },
  });
  if (complete.status !== 200) throw new SetupError(`registration/complete: ${complete.status} ${complete.text.slice(0, 160)}`);
  account.userId = complete.json.user?.id ?? complete.json.User?.Id;
  return {
    email,
    userId: account.userId,
    token: complete.json.token?.accessToken,
    refreshToken: complete.json.token?.refreshToken,
  };
}

function record(name, verdict, detail) {
  verdicts.push({ name, verdict });
  console.log(`${verdict.padEnd(12)} ${name}${detail ? ` - ${detail}` : ""}`);
}

/** Runs steps in order; the first that does not hold ends the scenario FAIL. */
async function steps(name, list) {
  for (const [label, check] of list) {
    const problem = await check();
    if (problem) { record(name, "FAIL", `${label}: ${problem}`); return false; }
    console.log(`  ok  ${label}`);
  }
  record(name, "PASS");
  return true;
}

const expect = (response, status, code) =>
  response.status === status && (code === undefined || response.code === code)
    ? null
    : `answered ${response.status} ${response.code ?? ""}, expected ${status} ${code ?? ""}`;

const rowIs = async (userId, want) => {
  const row = await lockRow(userId);
  return row.status === want.status && row.count === want.count && row.end === want.end
    ? null : `row ${show(row)}, expected ${show(want)}`;
};

const waitOutTheLock = () => sleep((LOCK_MINUTES * 60 + 5) * 1000);

// ── Scenarios ────────────────────────────────────────────────────────────────
async function cadence() {
  const owner = await register("cadence", FAMILIAR);
  const wrong = () => login(owner.email, WRONG, STRANGER);
  await steps("cadence", [
    [`${MAX} wrong passwords lock for the duration`, async () => {
      for (let i = 0; i < MAX; i++) {
        const r = await wrong();
        if (r.status !== 400 && r.status !== 401) return `failure ${i + 1} answered ${r.status} ${r.code}`;
      }
      return rowIs(owner.userId, { status: 3, count: MAX, end: "future" });
    }],
    ["a stranger under the timed lock is refused and not counted", async () =>
      expect(await wrong(), 403, "User.AccountLockedUntil") ?? rowIs(owner.userId, { status: 3, count: MAX, end: "future" })],
    ["after the lapse the count is kept", async () => {
      await waitOutTheLock();
      await wrong();
      return rowIs(owner.userId, { status: 1, count: MAX + 1, end: "none" });
    }],
    [`the ${HARD}th consecutive failure locks with no expiry`, async () => {
      for (let n = MAX + 2; n <= HARD; n++) {
        if (n > MAX + 1 && (n - 1) % MAX === 0 && n - 1 < HARD) await waitOutTheLock();
        await wrong();
      }
      return rowIs(owner.userId, { status: 3, count: HARD, end: "none" });
    }],
    ["a stranger with the right password is told to reset", async () =>
      expect(await login(owner.email, PASSWORD, STRANGER), 403, "User.AccountLockedResetRequired")],
    ["waiting does not end the hard lock", async () => {
      await waitOutTheLock();
      return expect(await login(owner.email, PASSWORD, STRANGER), 403, "User.AccountLockedResetRequired")
        ?? rowIs(owner.userId, { status: 3, count: HARD, end: "none" });
    }],
    ["the owner's session still renews, and lifts nothing", async () => {
      const r = await call("/api/v1/auth/refresh", { method: "POST", body: { refreshToken: owner.refreshToken }, from: FAMILIAR });
      return expect(r, 200) ?? rowIs(owner.userId, { status: 3, count: HARD, end: "none" });
    }],
    ["the familiar address signs in and the row is clear", async () =>
      expect(await login(owner.email, PASSWORD, FAMILIAR), 200) ?? rowIs(owner.userId, { status: 1, count: 0, end: "none" })],
  ]);
}

async function adminToken() {
  if (!ADMIN.email || !ADMIN.password) return null;
  const r = await login(ADMIN.email, ADMIN.password, FAMILIAR);
  return r.status === 200 ? r.json.token?.accessToken : null;
}

async function exits() {
  const owner = await register("exits", FAMILIAR);
  const hard = () => sql(`UPDATE dbo.Users SET Status = 3, FailedLoginAttempts = ${HARD}, LockoutEndUtc = NULL WHERE Id = '${owner.userId}'`);
  let password = PASSWORD;
  const clear = { status: 1, count: 0, end: "none" };
  const passed = await steps("exits", [
    ["change-password lifts the hard lock", async () => {
      await hard();
      const r = await call("/api/v1/auth/change-password", {
        method: "POST", token: owner.token, from: FAMILIAR,
        body: { currentPassword: password, newPassword: OTHER_PASSWORD, confirmNewPassword: OTHER_PASSWORD, terminateSessions: false },
      });
      password = OTHER_PASSWORD;
      return expect(r, 204) ?? rowIs(owner.userId, clear);
    }],
    ["a completed reset lifts the hard lock", async () => {
      await hard();
      const mark = logMark();
      const ask = await call("/api/v1/auth/forgot-password", { method: "POST", body: { email: owner.email }, from: FAMILIAR });
      if (ask.status >= 400) return `forgot-password answered ${ask.status}`;
      const token = await resetTokenAfter(mark);
      const r = await call("/api/v1/auth/reset-password", {
        method: "POST", from: FAMILIAR, body: { token, newPassword: PASSWORD, confirmNewPassword: PASSWORD, terminateSessions: false },
      });
      password = PASSWORD;
      return (r.status >= 200 && r.status < 300 ? null : `reset answered ${r.status} ${r.code}`) ?? rowIs(owner.userId, clear);
    }],
    ["the familiar address lifts the hard lock (before the seal)", async () => {
      await hard();
      return expect(await login(owner.email, password, FAMILIAR), 200) ?? rowIs(owner.userId, clear);
    }],
  ]);
  if (!passed) return;

  const admin = await adminToken();
  if (!admin) {
    record("exits (administrator)", "INCONCLUSIVE", "set PROBE_ADMIN_EMAIL and PROBE_ADMIN_PASSWORD (users:manage, no second factor)");
    return;
  }
  await steps("exits (administrator)", [
    ["Unlock lifts the hard lock and zeroes the count", async () => {
      await hard();
      return expect(await call(`/api/v1/users/${owner.userId}/unlock`, { method: "POST", token: admin, from: FAMILIAR }), 204)
        ?? rowIs(owner.userId, clear);
    }],
    ["Deactivate refuses the next sign-in", async () => {
      const r = await call(`/api/v1/users/${owner.userId}/deactivate`, { method: "POST", token: admin, from: FAMILIAR });
      if (r.status >= 300) return `deactivate answered ${r.status}`;
      return expect(await login(owner.email, password, FAMILIAR), 403, "User.AccountInactive");
    }],
    ["Activate clears the status and the count", async () => {
      await sql(`UPDATE dbo.Users SET FailedLoginAttempts = 7 WHERE Id = '${owner.userId}'`);
      const r = await call(`/api/v1/users/${owner.userId}/activate`, { method: "POST", token: admin, from: FAMILIAR });
      if (r.status >= 300) return `activate answered ${r.status}`;
      return rowIs(owner.userId, clear);
    }],
  ]);
}

async function pendingDeletion() {
  const owner = await register("deletion", FAMILIAR);
  const mark = logMark();
  const ask = await call("/api/v1/auth/deletion/request", { method: "POST", body: { email: owner.email }, from: FAMILIAR });
  if (ask.status !== 202) throw new SetupError(`deletion/request: ${ask.status}`);
  const otp = await fromLogAfter(mark, /deletion OTP for [^\s:]+:\s*(\d{6})/g, "deletion code");
  const confirm = await call("/api/v1/auth/deletion/confirm", { method: "POST", body: { email: owner.email, otpCode: otp }, from: FAMILIAR });
  if (confirm.status !== 202) throw new SetupError(`deletion/confirm: ${confirm.status} ${confirm.text.slice(0, 160)}`);

  const A = "203.0.113.61";
  const B = "203.0.113.62";
  const recover = (password, from) =>
    paced("/api/v1/auth/deletion/recover", { method: "POST", body: { email: owner.email, password }, from });
  const sourceLocked = async () => Number(await sql(
    `SELECT COUNT(1) FROM dbo.LoginAttempts WHERE UserId = '${owner.userId}' AND IpAddress = '${A}' AND FailureReason = N'Source locked'`));

  await steps("pending-deletion", [
    [`${MAX} wrong passwords from one address are answered generically and recorded`, async () => {
      for (let i = 0; i < MAX; i++) {
        const problem = expect(await recover(WRONG, A), 400, "User.InvalidCredentials");
        if (problem) return `attempt ${i + 1} ${problem}`;
      }
      const rows = Number(await sql(
        `SELECT COUNT(1) FROM dbo.LoginAttempts WHERE UserId = '${owner.userId}' AND IpAddress = '${A}' AND FailureReason = N'Invalid password'`));
      return rows === MAX ? null : `${rows} wrong-password rows from A, expected ${MAX}`;
    }],
    ["the next one from that address is refused before the hash", async () =>
      expect(await recover(PASSWORD, A), 400, "User.InvalidCredentials")
        ?? ((await sourceLocked()) === 1 ? null : "no 'Source locked' row: the ceiling did not refuse it")],
    ["the account was never locked", async () => rowIs(owner.userId, { status: 1, count: 0, end: "none" })],
    ["another address recovers with the right password", async () => expect(await recover(PASSWORD, B), 200)],
  ]);
}

// ── Revert ────────────────────────────────────────────────────────────────────
function revertBlock({ email }) {
  const byUser = [
    "UserEncryptionKeys", "RefreshTokens", "UserSessions", "IdpSessions", "AuthorizationCodes",
    "UserExternalLogins", "EmailVerificationTokens", "PasswordResetTokens", "AccountDeletionVerifications",
    "AccountDeletionRequests", "PasswordHistory", "TwoFactorChallenges", "TwoFactorAuth", "UserRoles",
    "UserPermissions", "ApplicationUserAccess", "OrganizationUserRoles", "OrganizationUserPermissions",
    "OrganizationUsers", "UserUiPreferences", "UserKnownDevices", "LoginAttempts",
  ];
  return [
    "SET XACT_ABORT ON;", "BEGIN TRAN;",
    `DECLARE @UserId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Users WHERE LOWER(Email) = LOWER('${email}'));`,
    `DELETE FROM dbo.NotificationOutbox WHERE RecipientUserId = @UserId OR Recipient = '${email}';`,
    ...byUser.map((t) => `IF OBJECT_ID(N'dbo.${t}', N'U') IS NOT NULL DELETE FROM dbo.${t} WHERE UserId = @UserId;`),
    "DELETE FROM dbo.AuditLogs WHERE UserId = @UserId OR PerformedBy = @UserId;",
    `DELETE FROM dbo.PendingRegistrations WHERE LOWER(Email) = LOWER('${email}');`,
    "DELETE FROM dbo.Users WHERE Id = @UserId;",
    "COMMIT;",
  ].join("\n");
}

async function cleanup() {
  for (const account of accounts) {
    try { await sql(revertBlock(account)); } catch (error) {
      console.error(`cleanup failed for ${account.email}: ${(error.stdout ?? "") + (error.stderr ?? "") || error.message}`);
    }
  }
  if (accounts.length) {
    console.log("\n--- revert SQL (already run by this probe; re-run to undo any row it left) ---");
    for (const account of accounts) console.log(`-- ${account.email}\n${revertBlock(account)}\nGO`);
  }
}

const scenarios = { cadence, exits, "pending-deletion": pendingDeletion };
const requested = process.argv[2];

async function main() {
  const names = requested === "all" ? Object.keys(scenarios) : [requested];
  if (!names.every((n) => scenarios[n])) {
    console.error(`Usage: node Tools/probes/account-lock.mjs <${Object.keys(scenarios).join("|")}|all>`);
    process.exit(2);
  }
  for (const [index, name] of names.entries()) {
    if (index > 0) { console.log("--- waiting 61 s for fresh rate windows ---"); await sleep(61_000); }
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
  console.error(`\n${error instanceof SetupError ? "setup aborted" : "probe aborted"}: ${error.message}`);
  await cleanup();
  process.exit(3);
});
