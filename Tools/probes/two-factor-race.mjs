/**
 * Live proof that the second-factor guessing limits hold under real concurrency,
 * against a running dev API and the dev database. A unit test pins the SQL; this
 * pins what SQL Server actually does when many requests arrive at once.
 *
 * Three scenarios, each the positional argument:
 *   burst        login, then 15 wrong TOTP codes at once  => exactly 5 answers of
 *                User.InvalidTwoFactorCode, and the account row shows
 *                FailedAttempts = 5 with the lock set.
 *   recovery     two logins (two challenges), the first reopened by SQL, then one
 *                recovery code on both at once => exactly one 200, and 9 of the 10
 *                recovery codes remain (the code was spent once).
 *   verify-email 15 wrong email OTPs at once for an unconfirmed account => the
 *                token row shows AttemptCount = 5 exactly.
 *
 * Anything not PASS is FAIL. Run each scenario in its own fresh 60-second rate
 * window; `all` runs the three with the wait between them.
 *
 * localhost only. The script creates throwaway accounts through the API's own
 * flows, reads the one-time codes from the API's Serilog file (Email disabled in
 * dev), never decrypts a stored secret, never prints a secret or a recovery code,
 * and prints the revert SQL for every row it changes.
 *
 * Environment (nothing is a literal in this public repo):
 *   PROBE_API_URL     dev API origin, e.g. https://localhost:5201 (must be localhost)
 *   PROBE_LOGS_DIR    the API's Logs directory (default: ../..//Auth/Auth_API/Logs from here)
 *   PROBE_SQLCMD_DB   database name          (default: Astoom_Auth)
 *   PROBE_SQLCMD_SRV  server                 (default: localhost\SQLEXPRESS01)
 *
 * Usage:
 *   node Tools/probes/two-factor-race.mjs <burst|recovery|verify-email|all>
 */
import { createHmac } from "node:crypto";
import { execFileSync } from "node:child_process";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const API = process.env.PROBE_API_URL ?? "https://localhost:5201";
const LOGS = process.env.PROBE_LOGS_DIR ?? resolve(HERE, "..", "..", "Auth", "Auth_API", "Logs");
const DB = process.env.PROBE_SQLCMD_DB ?? "Astoom_Auth";
const SERVER = process.env.PROBE_SQLCMD_SRV ?? "localhost\\SQLEXPRESS01";
const PASSWORD = "Pr0be-" + Math.random().toString(36).slice(2, 10) + "!Zq";

// localhost only, whatever the environment says.
const host = new URL(API).hostname;
if (!["localhost", "127.0.0.1", "::1"].includes(host)) {
  console.error(`Refusing to run against ${host}: this probe is localhost-only.`);
  process.exit(2);
}

// Node's fetch would reject the ASP.NET dev certificate; this is a throwaway
// localhost run, so trust it for this process only.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

const revertSql = [];
let failures = 0;

const record = (name, passed, detail) => {
  if (!passed) failures++;
  console.log(`${passed ? "PASS" : "FAIL"}  ${name.padEnd(42)} ${detail}`);
};

// ── TOTP (RFC 6238: SHA1, 30s step, 6 digits), to mint one valid code and to
//    keep the 15 wrong ones clear of the ±1 window the server accepts. ────────
function base32Decode(secret) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const ch of secret.replace(/=+$/, "").toUpperCase()) {
    const value = alphabet.indexOf(ch);
    if (value < 0) continue;
    bits += value.toString(2).padStart(5, "0");
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
  const code = ((hmac[offset] & 0x7f) << 24) | (hmac[offset + 1] << 16) | (hmac[offset + 2] << 8) | hmac[offset + 3];
  return (code % 1_000_000).toString().padStart(6, "0");
}

function totpNow(secret) {
  const key = base32Decode(secret);
  const step = Math.floor(Date.now() / 1000 / 30);
  return hotp(key, step);
}

function wrongCodes(secret, count) {
  const key = base32Decode(secret);
  const step = Math.floor(Date.now() / 1000 / 30);
  const valid = new Set([-1, 0, 1].map((d) => hotp(key, step + d)));
  const codes = [];
  for (let n = 0; codes.length < count; n++) {
    const candidate = n.toString().padStart(6, "0");
    if (!valid.has(candidate)) codes.push(candidate);
  }
  return codes;
}

// ── HTTP ─────────────────────────────────────────────────────────────────────
async function call(path, { method = "GET", body, token } = {}) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: {
      "content-type": "application/json",
      ...(token ? { authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON body */ }
  return { status: response.status, json, text };
}

const errorCode = (result) => result.json?.code ?? result.json?.errors?.[0]?.code ?? `HTTP ${result.status}`;

// ── Serilog: read a one-time code the API logged (Email disabled in dev). The
//    log masks the address, so capture the file length first and scan only what
//    the triggering call appended. ─────────────────────────────────────────────
function logLength() {
  const files = readdirSync(LOGS).filter((f) => f.endsWith(".log")).map((f) => join(LOGS, f));
  if (files.length === 0) return { file: null, length: 0 };
  const file = files.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
  return { file, length: statSync(file).size };
}

function otpAfter(mark, { tries = 40, delayMs = 150 } = {}) {
  return new Promise((resolveOtp, reject) => {
    let attempt = 0;
    const poll = () => {
      const now = logLength();
      const file = now.file ?? mark.file;
      if (file) {
        const appended = readFileSync(file, "utf8").slice(mark.length);
        // Serilog's file template here prints properties unquoted
        // (OTP for x****2@host: 880698); tolerate quotes in case it changes.
        const matches = [...appended.matchAll(/OTP for "?[^\s":]+"?:\s*"?(\d{6})"?/g)];
        if (matches.length > 0) return resolveOtp(matches[matches.length - 1][1]);
      }
      if (++attempt >= tries) return reject(new Error("no OTP appeared in the API log"));
      setTimeout(poll, delayMs);
    };
    poll();
  });
}

// ── sqlcmd ───────────────────────────────────────────────────────────────────
function sql(query) {
  const out = execFileSync(
    "sqlcmd",
    ["-S", SERVER, "-d", DB, "-E", "-I", "-W", "-h", "-1", "-s", "|", "-Q", `SET NOCOUNT ON; ${query}`],
    { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] },
  );
  return out.trim();
}

// ── Account setup through the API's own flows ─────────────────────────────────
async function register(email) {
  const mark = logLength();
  const start = await call("/api/v1/auth/registration/start", {
    method: "POST",
    body: { email, preferredLanguage: "en" },
  });
  if (start.status !== 200) throw new Error(`registration/start: ${start.status} ${start.text.slice(0, 200)}`);
  const otp = await otpAfter(mark);
  const pendingId = start.json.pendingId ?? start.json.PendingId;

  const verify = await call("/api/v1/auth/registration/verify", { method: "POST", body: { pendingId, otp } });
  if (verify.status !== 204) throw new Error(`registration/verify: ${verify.status} ${verify.text.slice(0, 200)}`);

  const complete = await call("/api/v1/auth/registration/complete", {
    method: "POST",
    body: { pendingId, otp, password: PASSWORD, firstName: "Probe", lastName: "User", createOrganization: false },
  });
  if (complete.status !== 200) throw new Error(`registration/complete: ${complete.status} ${complete.text.slice(0, 200)}`);
  const token = complete.json.token?.accessToken ?? complete.json.Token?.AccessToken;
  const userId = complete.json.user?.id ?? complete.json.User?.Id;
  revertSql.push(`DELETE FROM dbo.Users WHERE Id = '${userId}'; -- probe account ${email}`);
  return { token, userId };
}

async function enableTwoFactor(token) {
  const setup = await call("/api/v1/auth/2fa/setup", { method: "POST", token });
  if (setup.status !== 200) throw new Error(`2fa/setup: ${setup.status} ${setup.text.slice(0, 200)}`);
  const secret = setup.json.secret ?? setup.json.Secret;

  const enable = await call("/api/v1/auth/2fa/enable", { method: "POST", token, body: { code: totpNow(secret) } });
  if (enable.status !== 200) throw new Error(`2fa/enable: ${enable.status} ${enable.text.slice(0, 200)}`);
  const recoveryCodes = enable.json.recoveryCodes ?? enable.json.RecoveryCodes;
  return { secret, recoveryCodes };
}

async function loginForChallenge(email) {
  const login = await call("/api/v1/auth/login", { method: "POST", body: { email, password: PASSWORD } });
  if (login.status !== 200) throw new Error(`login: ${login.status} ${login.text.slice(0, 200)}`);
  return login.json.twoFactorChallengeToken ?? login.json.TwoFactorChallengeToken;
}

const uniqueEmail = (tag) => `x08-${tag}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}@x08.probe.local`;

// ── Scenarios ─────────────────────────────────────────────────────────────────
async function burst() {
  const email = uniqueEmail("burst");
  const { token, userId } = await register(email);
  const { secret } = await enableTwoFactor(token);
  const challengeToken = await loginForChallenge(email);

  const codes = wrongCodes(secret, 15);
  const answers = await Promise.all(codes.map((code) =>
    call("/api/v1/auth/2fa/verify", { method: "POST", body: { challengeToken, code } }).then(errorCode)));

  const invalidCount = answers.filter((code) => code === "User.InvalidTwoFactorCode").length;
  const row = sql(`SELECT FailedAttempts, IIF(LockedUntil > SYSUTCDATETIME(), 1, 0) FROM dbo.TwoFactorAuth WHERE UserId = '${userId}'`);
  const [failed, locked] = row.split("|").map((s) => s.trim());

  record("burst: exactly 5 codes checked", invalidCount === 5, `InvalidTwoFactorCode answers = ${invalidCount} (others: ${answers.filter((c) => c !== "User.InvalidTwoFactorCode").join(",")})`);
  record("burst: account counter = 5", failed === "5", `FailedAttempts = ${failed}`);
  record("burst: factor locked", locked === "1", `locked = ${locked}`);
}

async function recovery() {
  const email = uniqueEmail("recovery");
  const { token, userId } = await register(email);
  const { recoveryCodes } = await enableTwoFactor(token);
  const code = recoveryCodes[0];

  // Two sequential logins: the second supersedes the first challenge.
  const first = await loginForChallenge(email);
  const second = await loginForChallenge(email);

  // Reopen the first challenge so one recovery code races on two live challenges.
  const firstHash = sql(`SELECT TOP 1 Id FROM dbo.TwoFactorChallenges WHERE UserId = '${userId}' AND UsedAt IS NOT NULL ORDER BY CreatedAt`);
  console.log(`      (reopened challenge ${firstHash} for the race; see revert SQL)`);
  sql(`UPDATE dbo.TwoFactorChallenges SET UsedAt = NULL WHERE UserId = '${userId}'`);
  revertSql.push(`-- the two race challenges for ${email} are deleted with the account row above`);

  const answers = await Promise.all([first, second].map((challengeToken) =>
    call("/api/v1/auth/2fa/verify", { method: "POST", body: { challengeToken, code, useRecoveryCode: true } })));
  const ok = answers.filter((a) => a.status === 200).length;

  // Count elements by their Argon2id prefix, not by commas: a PHC hash carries
  // its own commas (m=19456,t=2,p=1), so comma-counting overcounts.
  const remaining = sql(`SELECT (LEN(RecoveryCodes) - LEN(REPLACE(RecoveryCodes, '$argon2id$', ''))) / LEN('$argon2id$') FROM dbo.TwoFactorAuth WHERE UserId = '${userId}' AND RecoveryCodes IS NOT NULL`);

  record("recovery: one sign-in wins", ok === 1, `200 answers = ${ok}`);
  record("recovery: code spent once (9 remain)", remaining === "9", `remaining codes = ${remaining}`);
}

async function verifyEmail() {
  // An unconfirmed account with a live email-verification token. The verify-first
  // flow confirms on completion, so create the account, drop its confirmed flag,
  // and let the resend endpoint mint the token the verify path reads.
  const email = uniqueEmail("vmail");
  const { userId } = await register(email);
  sql(`UPDATE dbo.Users SET IsEmailConfirmed = 0 WHERE Id = '${userId}'`);

  const resend = await call("/api/v1/auth/resend-verification-email", { method: "POST", body: { email } });
  if (resend.status !== 200) throw new Error(`resend-verification-email: ${resend.status} ${resend.text.slice(0, 200)}`);

  const codes = wrongCodes("JBSWY3DPEHPK3PXP", 15); // 15 distinct 6-digit codes; all wrong
  await Promise.all(codes.map((otp) =>
    call("/api/v1/auth/verify-email", { method: "POST", body: { email, otp } })));

  // The counter alone is self-capping (the reserve's WHERE physically bars a
  // sixth), so it would read 5 even if the handler mishandled the code. Pin the
  // end-to-end outcome too: no wrong code confirmed the address or spent the
  // token.
  const row = sql(`SELECT t.AttemptCount, u.IsEmailConfirmed, IIF(t.UsedAt IS NULL, 1, 0)
                   FROM dbo.EmailVerificationTokens t JOIN dbo.Users u ON u.Id = t.UserId
                   WHERE t.UserId = '${userId}' AND t.UsedAt IS NULL`);
  const [attempts, confirmed, unused] = row.split("|").map((s) => s.trim());
  record("verify-email: attempts capped at 5", attempts === "5", `AttemptCount = ${attempts}`);
  record("verify-email: no wrong code confirmed or spent", confirmed === "0" && unused === "1", `IsEmailConfirmed = ${confirmed}, token unused = ${unused}`);
}

// ── Driver ───────────────────────────────────────────────────────────────────
const scenario = process.argv[2];
const scenarios = { burst, recovery, "verify-email": verifyEmail };

async function main() {
  const wait = (seconds) => new Promise((r) => setTimeout(r, seconds * 1000));

  if (scenario === "all") {
    for (const [index, name] of Object.keys(scenarios).entries()) {
      if (index > 0) { console.log(`--- waiting 61s for a fresh rate window before ${name} ---`); await wait(61); }
      console.log(`=== ${name} ===`);
      await scenarios[name]();
    }
  } else if (scenarios[scenario]) {
    await scenarios[scenario]();
  } else {
    console.error("Usage: node Tools/probes/two-factor-race.mjs <burst|recovery|verify-email|all>");
    process.exit(2);
  }

  console.log("\n--- revert SQL (run against the dev database to undo every row this probe changed) ---");
  for (const line of revertSql) console.log(line);
  console.log(`\n${failures === 0 ? "ALL PASS" : `${failures} FAIL`}`);
  process.exit(failures === 0 ? 0 : 1);
}

main().catch((error) => {
  console.error(`\nprobe aborted: ${error.message}`);
  if (revertSql.length > 0) {
    console.log("\n--- revert SQL (partial; run to undo what was created before the abort) ---");
    for (const line of revertSql) console.log(line);
  }
  process.exit(3);
});
