/**
 * Live proof that rotating the credential HMAC key signs nobody out (S11 PR B),
 * against a running API built from the branch and a THROWAWAY database. The
 * xUnit suite pins each transition and each lookup site; this shows the whole
 * path, from a credential handed out over HTTP to its lookup after the keyring
 * moved. A check that cannot fail proves nothing, so run it once more with
 * legacy acceptance disabled (the deliberate break below) and the run must FAIL
 * at "after promote".
 *
 * What it does, in order. Each "set" is one credential of every class the
 * keyring fingerprints and the API stores for a lookup, issued over HTTP:
 *   refresh token · SSO session (the auth_idp cookie) · parked authorize request
 *   (its handle) · authorization code · password-reset link · organization
 *   invitation · pending two-factor sign-in · pending registration (its handle
 *   and its emailed code)
 * plus one webhook key, made once, which must outlive even an emergency.
 *   1. The ring must start where every deployment upgrading to this build
 *      starts: key 1 current, never rotated. Set A is issued under key 1's
 *      legacy form.
 *   2. Stage key 2, promote it. Every credential of set A is accepted: the
 *      legacy form keeps verifying. The webhook key validates and is
 *      re-fingerprinted under key 2 (sqlcmd reads hk2$ on its row).
 *   3. Set B, under key 2. Reinstate key 1 (the rollback). Set B is accepted.
 *      Key 1 never derives a subkey, so it issues its legacy form again: the
 *      webhook row goes back to that form on its next use.
 *   4. Set C, under key 1's legacy form again. Stage key 3, promote it (a second
 *      promote). Set C is accepted.
 *   5. Set D, under key 3. Emergency rotation (key 4; every older key is marked
 *      leaked). Every credential of set D is REFUSED, while the webhook key
 *      still validates and is re-fingerprinted under key 4 (hk4$).
 * Each keyring change goes through the API's own three steps: a challenge, the
 * emailed code (read from the API's log, Email off) with the change named, and
 * the change itself. Nothing touches the secrets file but the API.
 *
 * Deliberate break: in Auth/Auth.Application/Security/Keyring/HmacKeyring.cs,
 * Accepted(), make the legacy branch unreachable (`if (false && !Issuing.IsLegacy`),
 * rebuild, restore the never-rotated secrets file, run again: the run must FAIL
 * at "after promote" (set A refused), exit 1.
 *
 * Verdicts: PASS · FAIL (a credential answered wrong, a 5xx, an unexpected code)
 * · INCONCLUSIVE (the ring had already rotated, or a 429 outlived one wait).
 *
 * Safety. This rotates the keyring of the API it talks to, and its last step is
 * an emergency rotation, which signs out every user of that API. So it runs only:
 *   - against a localhost API and a local SQL Server (LocalDB included);
 *   - with PROBE_KEYRING_THROWAWAY=yes, which states that the API runs on a
 *     throwaway secrets file and a throwaway database;
 *   - on a ring that never rotated (else INCONCLUSIVE, nothing changed).
 * An account is a confirmed row with no password, written to the throwaway
 * database, that takes its first password through the API's own "forgot password"
 * and reset (registration/start refuses while email is off, OI-215). Without
 * PROBE_ADMIN_EMAIL and PROBE_ADMIN_PASSWORD it makes its own administrator that
 * way and grants it the platform's Super Administrator role there. The pending
 * registration under test comes from registration/start itself, with email
 * sending switched on through System settings for that one request and switched
 * back after, so the API keeps its database settings layer on (never
 * AUTH_DISABLE_DB_SETTINGS=true) and the administrator holds
 * system-settings:manage. One-time codes
 * and the reset link come from the API's Serilog file (Email off, Development);
 * so does the invitation link, from the rendered email the API writes at Debug
 * in Development only (its notification row is redacted once delivered). No
 * secret, code or token is printed.
 *
 * Run the API (ASPNETCORE_ENVIRONMENT=Development, Email off) with:
 *   Email__MaxOtpRequestsPerWindow=20  the six keyring changes would otherwise meet
 *                                      the per-administrator code limit (3 a minute);
 *   Serilog__MinimumLevel__Override__Auth.Infrastructure.Notifications=Debug
 *                                      the rendered invitation email reaches the log.
 *
 * Env: PROBE_API_URL (default https://localhost:5101, localhost only),
 * PROBE_LOGS_DIR (default Auth/Auth_API/Logs), PROBE_SQLCMD_SRV (default
 * localhost\SQLEXPRESS01, a local server only), PROBE_SQLCMD_DB (default
 * AuthSystem), PROBE_KEYRING_THROWAWAY=yes (required), PROBE_ADMIN_EMAIL and
 * PROBE_ADMIN_PASSWORD (optional: an account with secrets.manage, webhook-key,
 * application, organization and system-settings:manage rights, and no second
 * factor).
 *
 * Usage: node Tools/probes/keyring-dual-verify.mjs
 * Exit:  0 PASS · 1 FAIL · 4 INCONCLUSIVE · 3 setup aborted · 2 refused
 */
import { createHash, createHmac, randomBytes } from "node:crypto";
import { execFile } from "node:child_process";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import { confirmedAccountSql, withEmailSendingOn } from "../lib/dev-accounts.mjs";

const execFileAsync = promisify(execFile);
const HERE = dirname(fileURLToPath(import.meta.url));
const API = process.env.PROBE_API_URL ?? "https://localhost:5101";
const LOGS = process.env.PROBE_LOGS_DIR ?? resolve(HERE, "..", "..", "Auth", "Auth_API", "Logs");
const SERVER = process.env.PROBE_SQLCMD_SRV ?? "localhost\\SQLEXPRESS01";
const DB = process.env.PROBE_SQLCMD_DB ?? "AuthSystem";
const PASSWORD = `Pr0be-${randomBytes(6).toString("hex")}!Zq`;
const RESET_PASSWORDS = [1, 2, 3, 4].map((n) => `Pr0be-${randomBytes(6).toString("hex")}!R${n}`);
// Read from the environment, never from a file: a password in a tracked file is
// published to everyone who can read the repository.
const ADMIN = { email: process.env.PROBE_ADMIN_EMAIL, password: process.env.PROBE_ADMIN_PASSWORD };
const REDIRECT_URI = "https://app.example.com/callback";

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
if (process.env.PROBE_KEYRING_THROWAWAY !== "yes") {
  console.error(
    "Refusing to run: this probe ends with an emergency rotation, which signs out every user of the API it targets. " +
      "Set PROBE_KEYRING_THROWAWAY=yes only for an API running on a throwaway secrets file and a throwaway database.",
  );
  process.exit(2);
}

// The host is checked above, so trust the dev certificate for this process only.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

class SetupError extends Error {}
class Inconclusive extends Error {}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const verdicts = [];

function record(name, verdict, detail) {
  verdicts.push({ name, verdict });
  console.log(`${verdict.padEnd(12)} ${name.padEnd(34)} ${detail}`);
}

// ── TOTP (RFC 6238: SHA1, 30 s, 6 digits) for the two-factor account ─────────
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

// A code is accepted once (X01): every use waits for a step no earlier use spent.
async function freshCode(secret, state) {
  while (step() <= state.lastStep) await sleep(500);
  state.lastStep = step();
  return hotp(base32Decode(secret), state.lastStep);
}

// ── PKCE (RFC 7636, S256) ────────────────────────────────────────────────────
function pkce() {
  const verifier = randomBytes(32).toString("base64url");
  const challenge = createHash("sha256").update(verifier).digest("base64url");
  return { verifier, challenge };
}

// ── HTTP ─────────────────────────────────────────────────────────────────────
async function call(path, { method = "GET", body, token, cookie, form } = {}) {
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  if (cookie) headers.cookie = cookie;
  let payload;
  if (form) {
    headers["content-type"] = "application/x-www-form-urlencoded";
    payload = new URLSearchParams(form).toString();
  } else if (body !== undefined) {
    headers["content-type"] = "application/json";
    payload = JSON.stringify(body);
  }
  const response = await fetch(`${API}${path}`, { method, headers, body: payload, redirect: "manual" });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON body */ }
  return {
    status: response.status,
    json,
    text,
    code: json?.code ?? null,
    location: response.headers.get("location"),
    setCookies: response.headers.getSetCookie(),
  };
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
  if (!statuses.includes(response.status)) {
    throw new SetupError(`${what}: ${response.status} ${response.code ?? response.text.slice(0, 160)}`);
  }
  return response;
}

// ── Serilog: codes and links the API logs with Email off. The lines mask the
//    address, so capture the file length first and scan only what was appended. ──
function logMark() {
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

const otpAfter = (mark) => fromLogAfter(mark, /OTP for "?[^\s":]+"?:\s*"?(\d{6})"?/g, "one-time code");
const resetTokenAfter = (mark) =>
  fromLogAfter(mark, /Password reset link for [^\s:]+:\s*\S*?[?&]token=([^&\s"]+)/g, "reset link").then(decodeURIComponent);
// The rendered body is logged at Debug, in Development only, with Email off, when
// the outbox dispatcher sends it: on its signal, or at the latest on its next poll
// (Notifications:PollIntervalSeconds, 30 by default).
const invitationTokenAfter = (mark) =>
  fromLogAfter(mark, /accept-invitation\?token=([^&\s"<]+)/g, "invitation link (is the Notifications namespace at Debug?)", 45_000)
    .then(decodeURIComponent);
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

// ── Accounts ─────────────────────────────────────────────────────────────────
// The probe keeps email off (codes and links come from the log), and since OI-215
// registration/start refuses every address while it is off. So an account starts
// as a confirmed row with no password, written here, takes its first password
// through the API's own "forgot password" and reset, and signs in. The pending
// registration under test is the exception: its code is a credential the keyring
// keys, so it comes from registration/start itself, with email sending switched on
// for that one request by the administrator and switched back after.
const uniqueEmail = (tag) => `kr-${tag}-${Date.now().toString(36)}${randomBytes(2).toString("hex")}@probe.example.com`;

async function startRegistration(email, admin) {
  const mark = logMark();
  const start = await withEmailSendingOn({ api: API, token: admin }, async () =>
    must(
      await paced("/api/v1/auth/registration/start", { method: "POST", body: { email, preferredLanguage: "en" } }),
      [200],
      "registration/start",
    ));
  const otp = await otpAfter(mark);
  return { email, pendingId: start.json.pendingId, otp };
}

async function completeRegistration(pending) {
  const verify = await call("/api/v1/auth/registration/verify", {
    method: "POST", body: { pendingId: pending.pendingId, otp: pending.otp },
  });
  if (verify.status !== 204) return { ok: false, detail: `verify ${verify.status} ${verify.code}` };
  const complete = await call("/api/v1/auth/registration/complete", {
    method: "POST",
    body: { pendingId: pending.pendingId, otp: pending.otp, password: PASSWORD, firstName: "Probe", lastName: "User" },
  });
  if (complete.status !== 200) return { ok: false, detail: `complete ${complete.status} ${complete.code}` };
  return { ok: true, userId: complete.json.user?.id, token: complete.json.token?.accessToken };
}

async function register(tag) {
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
  const signIn = must(await login(email, PASSWORD), [200], `${tag}: first sign-in`);
  if (!signIn.json.token?.accessToken) throw new SetupError(`${tag}: the first sign-in returned no access token`);
  return { email, userId, token: signIn.json.token.accessToken };
}

async function login(email, password) {
  return paced("/api/v1/auth/login", { method: "POST", body: { email, password } });
}

const idpCookieOf = (response) => {
  const raw = response.setCookies.find((c) => c.startsWith("auth_idp="));
  return raw ? raw.split(";")[0] : null;
};

// The administrator's credentials: the ones supplied, or a probe account made
// here. Returned rather than a token, because the organization rights it gains
// mid-run ride only in a token issued after them.
async function adminAccount() {
  if (ADMIN.email && ADMIN.password) return ADMIN;
  const admin = await register("admin");
  // The throwaway database only (PROBE_KEYRING_THROWAWAY): the platform's own
  // Super Administrator role, looked up by name, granted to the account just made.
  await sql(
    `INSERT INTO dbo.UserRoles (Id, UserId, RoleId, ApplicationId, AssignedAt, AssignedBy, IsActive)
     SELECT NEWID(), '${admin.userId}', r.Id, NULL, SYSUTCDATETIME(), '${admin.userId}', 1
     FROM dbo.Roles r WHERE r.Name = N'Super Administrator' AND r.ApplicationId IS NULL;`,
  );
  return { email: admin.email, password: PASSWORD };
}

async function adminToken(account) {
  const r = must(await login(account.email, account.password), [200], "administrator sign-in");
  if (!r.json.token?.accessToken) {
    throw new Inconclusive("the administrator was asked for a second factor; use an account without one");
  }
  return r.json.token.accessToken;
}

// ── Keyring changes, through the three steps the console takes ───────────────
async function keyring(admin) {
  return must(await call("/api/v1/admin/Secrets/keyring", { token: admin }), [200], "GET keyring").json;
}

const changeEndpoint = {
  Stage: () => ["/api/v1/admin/Secrets/keyring/hmac/stage", (challengeId) => ({ challengeId, value: null })],
  Promote: () => ["/api/v1/admin/Secrets/keyring/hmac/promote", (challengeId) => ({ challengeId, emergency: false })],
  PromoteEmergency: () => ["/api/v1/admin/Secrets/keyring/hmac/promote", (challengeId) => ({ challengeId, emergency: true })],
  Reinstate: (keyId) => [`/api/v1/admin/Secrets/keyring/hmac/reinstate/${keyId}`, (challengeId) => ({ challengeId })],
};

async function change(admin, action, keyId) {
  const namesItsKey = action === "Reinstate";
  const binding = { keyringAction: action, keyId: namesItsKey ? keyId : undefined };
  const mark = logMark();
  const challenge = must(
    await paced("/api/v1/admin/Secrets/challenges", {
      method: "POST", token: admin, body: { operation: "GenerateHmacKey", value: null, ...binding },
    }),
    [200, 201],
    `${action}: challenge`,
  );
  const challengeId = challenge.json.challengeId;
  const code = await secretCodeAfter(mark);
  must(
    await call(`/api/v1/admin/Secrets/challenges/${challengeId}/verify`, {
      method: "POST", token: admin, body: { code, ...binding },
    }),
    [200],
    `${action}: verify`,
  );
  const [path, body] = changeEndpoint[action](keyId);
  return must(await call(path, { method: "POST", token: admin, body: body(challengeId) }), [200], action).json;
}

// ── One credential of every stored class, issued now ─────────────────────────
async function issueSet(label, ctx) {
  const set = { label };

  // Refresh token and SSO session: one password sign-in of the subject.
  const signIn = must(await login(ctx.subject.email, PASSWORD), [200], `${label}: subject sign-in`);
  set.refreshToken = signIn.json.token?.refreshToken;
  set.idpCookie = idpCookieOf(signIn);
  if (!set.refreshToken || !set.idpCookie) throw new SetupError(`${label}: the sign-in returned no refresh token or no auth_idp cookie`);

  // Authorization code: the SSO session answers an authorize request at once.
  set.codePkce = pkce();
  const authorize = await call(authorizeUrl(ctx, set.codePkce.challenge), { cookie: set.idpCookie });
  set.code = codeFrom(authorize);
  if (!set.code) throw new SetupError(`${label}: authorize with the session answered ${authorize.status} ${authorize.code ?? ""}, not a code`);

  // Parked authorize request: the same request without a session is parked
  // under a handle and sent to the sign-in page.
  set.parkedPkce = pkce();
  const parked = await call(authorizeUrl(ctx, set.parkedPkce.challenge));
  set.handle = parked.location ? new URL(parked.location).searchParams.get("auth_request") : null;
  if (!set.handle) throw new SetupError(`${label}: authorize without a session answered ${parked.status}, not a parked request`);

  // Password-reset link, for the account kept for resets alone (a completed
  // reset ends that account's sessions, which must not touch the subject's).
  let mark = logMark();
  must(await paced("/api/v1/auth/forgot-password", { method: "POST", body: { email: ctx.resetter.email } }), [200, 202, 204], `${label}: forgot-password`);
  set.resetToken = await resetTokenAfter(mark);

  // Organization invitation, its link read from the rendered email in the log.
  const invitee = uniqueEmail(`inv${label}`);
  mark = logMark();
  must(
    await paced(`/api/v1/Organizations/${ctx.organizationId}/invitations`, {
      method: "POST", token: ctx.inviter, body: { email: invitee, roleId: ctx.memberRoleId },
    }),
    [200, 201],
    `${label}: invite`,
  );
  set.invitationToken = await invitationTokenAfter(mark);

  // Pending two-factor sign-in.
  const challenge = must(await login(ctx.twoFactor.email, PASSWORD), [200], `${label}: two-factor sign-in`);
  set.challengeToken = challenge.json.twoFactorChallengeToken;
  if (!set.challengeToken) throw new SetupError(`${label}: the two-factor account was not asked for a second factor`);

  // Pending registration: its handle and its emailed code.
  set.pending = await startRegistration(uniqueEmail(`reg${label}`), ctx.admin);
  return set;
}

function authorizeUrl(ctx, challenge) {
  const query = new URLSearchParams({
    response_type: "code",
    client_id: ctx.clientId,
    redirect_uri: REDIRECT_URI,
    code_challenge: challenge,
    code_challenge_method: "S256",
    state: randomBytes(8).toString("hex"),
    scope: "openid",
  });
  return `/api/v1/auth/authorize?${query}`;
}

function codeFrom(response) {
  if (response.status !== 302 || !response.location?.startsWith(REDIRECT_URI)) return null;
  return new URL(response.location).searchParams.get("code");
}

// ── Present every credential of a set; each answers accepted or refused ──────
async function present(set, ctx) {
  const outcome = {};
  const accepted = (ok, detail) => ({ ok, detail });

  const refresh = await call("/api/v1/auth/refresh", { method: "POST", body: { refreshToken: set.refreshToken } });
  outcome["refresh token"] = accepted(refresh.status === 200, `${refresh.status} ${refresh.code ?? ""}`);

  // The SSO session first: the parked request needs it to resume, so a refused
  // handle is told apart from a refused session.
  const sso = await call(authorizeUrl(ctx, pkce().challenge), { cookie: set.idpCookie });
  const ssoCode = codeFrom(sso);
  outcome["SSO session"] = accepted(Boolean(ssoCode), ssoCode ? "302 to the client with a code" : `${sso.status} to the sign-in page`);

  const resume = await call(
    `/api/v1/auth/authorize/resume?${new URLSearchParams({ client_id: ctx.clientId, auth_request: set.handle })}`,
    { cookie: ctx.liveCookie() },
  );
  const resumed = codeFrom(resume);
  outcome["parked authorize request"] = accepted(Boolean(resumed), resumed ? "302 to the client with a code" : `${resume.status} ${resume.code ?? "elsewhere"}`);

  const token = await call("/api/v1/auth/token", {
    method: "POST",
    form: {
      grant_type: "authorization_code", code: set.code, redirect_uri: REDIRECT_URI,
      client_id: ctx.clientId, code_verifier: set.codePkce.verifier,
    },
  });
  outcome["authorization code"] = accepted(token.status === 200, `${token.status} ${token.code ?? token.json?.error ?? ""}`);

  const newPassword = RESET_PASSWORDS[ctx.resets++];
  const reset = await call("/api/v1/auth/reset-password", {
    method: "POST", body: { token: set.resetToken, newPassword, confirmNewPassword: newPassword, terminateSessions: false },
  });
  outcome["password-reset link"] = accepted(reset.status >= 200 && reset.status < 300, `${reset.status} ${reset.code ?? ""}`);

  const invitation = await call(`/api/v1/Invitations/${encodeURIComponent(set.invitationToken)}`);
  outcome["organization invitation"] = accepted(invitation.status === 200, `${invitation.status} ${invitation.code ?? ""}`);

  const verify = await call("/api/v1/auth/2fa/verify", {
    method: "POST", body: { challengeToken: set.challengeToken, code: await freshCode(ctx.twoFactor.secret, ctx.totp) },
  });
  outcome["pending two-factor sign-in"] = accepted(verify.status === 200, `${verify.status} ${verify.code ?? ""}`);

  const registration = await completeRegistration(set.pending);
  outcome["pending registration + code"] = accepted(registration.ok, registration.ok ? "verified and completed" : registration.detail);

  return outcome;
}

// Key 1 issues its legacy form, which has no prefix; every later key, hk{id}$.
const LEGACY_FORM = "(legacy form)";
const prefixOf = (keyId) => (keyId === 1 ? LEGACY_FORM : `hk${keyId}$`);

async function webhookUse(ctx) {
  const r = await call("/api/v1/WebhookKeys/validate", { method: "POST", token: ctx.admin, body: { webhookKey: ctx.webhookKey } });
  // The re-fingerprint is written by an event handler after the answer; give it a moment.
  let prefix = "";
  for (let attempt = 0; attempt < 20; attempt++) {
    const hash = await sql(`SELECT KeyHash FROM dbo.WebhookKeys WHERE Id = '${guid(ctx.webhookKeyId)}';`);
    prefix = hash.match(/^(hk\d+\$)/)?.[1] ?? LEGACY_FORM;
    if (prefix === prefixOf(ctx.expectedHashKey)) break;
    await sleep(250);
  }
  return { ok: r.status === 200 && r.json?.active === true, status: `${r.status}${r.code ? ` ${r.code}` : ""}`, prefix };
}

// Every credential of the set must be accepted (expect true) or refused (false),
// and the webhook key must validate with its row under the issuing key.
function judge(name, outcome, expectAccepted, webhook, expectedPrefix) {
  const wrong = Object.entries(outcome).filter(([, o]) => o.ok !== expectAccepted);
  const lines = Object.entries(outcome).map(([cls, o]) => `${cls}: ${o.ok ? "accepted" : "refused"} (${o.detail.trim()})`);
  const webhookOk = webhook.ok && webhook.prefix === expectedPrefix;
  lines.push(`webhook key: ${webhook.ok ? "validates" : "REFUSED"} (${webhook.status}), row ${webhook.prefix}, expected ${expectedPrefix}`);
  const verdict = wrong.length === 0 && webhookOk ? "PASS" : "FAIL";
  const expected = expectAccepted ? "accepted" : "refused";
  const classes = Object.keys(outcome).length;
  record(
    name,
    verdict,
    `${classes - wrong.length} of ${classes} credentials ${expected} as expected; webhook ${webhookOk ? "as expected" : "WRONG"}`,
  );
  for (const line of lines) console.log(`             - ${line}`);
  return verdict === "PASS";
}

// ── The run ───────────────────────────────────────────────────────────────────
async function main() {
  const adminCredentials = await adminAccount();
  let admin = await adminToken(adminCredentials);
  const start = await keyring(admin);
  if (Number(start.currentKeyId) !== 1 || start.legacyCutoverUtc || start.keys.length !== 1) {
    throw new Inconclusive(
      `the ring has already rotated (current key ${start.currentKeyId}, ${start.keys.length} keys); ` +
        "restore the never-rotated throwaway secrets file and restart the API",
    );
  }

  // Accounts and the client application the credentials belong to.
  const subject = await register("subject");
  const resetter = await register("reset");
  const twoFactor = await register("2fa");
  const setup = must(await call("/api/v1/auth/2fa/setup", { method: "POST", token: twoFactor.token }), [200], "2fa/setup");
  if (setup.json.emailCodeRequired === true) {
    throw new Inconclusive("2fa/setup asks for the emailed code (Email on); run the API with Email off");
  }
  twoFactor.secret = setup.json.secret;
  const totp = { lastStep: 0 };
  must(
    await call("/api/v1/auth/2fa/enable", { method: "POST", token: twoFactor.token, body: { code: await freshCode(twoFactor.secret, totp) } }),
    [200],
    "2fa/enable",
  );

  const clientId = `kr-probe-${Date.now().toString(36)}`;
  const app = must(
    await call("/api/v1/Applications", {
      method: "POST", token: admin,
      body: { code: clientId, name: "Keyring probe client", redirectUris: [REDIRECT_URI], accessMode: "Everyone", allowedScopes: ["openid"] },
    }),
    [201],
    "create the client application",
  ).json;

  const organization = must(
    await call("/api/v1/Organizations", {
      method: "POST", token: admin,
      body: { code: `kr-probe-${Date.now().toString(36)}`, name: "Keyring probe organization", contactEmail: "owner@probe.example.com" },
    }),
    [200, 201],
    "create the organization",
  ).json;
  const memberRoleId = guid(await sql("SELECT TOP 1 Id FROM dbo.Roles WHERE Name = N'Organization Member' AND ApplicationId IS NULL;"));
  // The creator's organization rights ride in the token: sign in again to carry them.
  admin = await adminToken(adminCredentials);

  const webhook = must(
    await call("/api/v1/WebhookKeys", {
      method: "POST", token: admin,
      body: { applicationId: app.id, name: "keyring probe", targetUrl: "https://hooks.example.com/probe" },
    }),
    [201],
    "create the webhook key",
  ).json;

  // A live session of the subject, for resuming parked requests: refreshed after
  // every change, so it is never the credential under test.
  let liveCookie = null;
  const ctx = {
    admin, inviter: admin, subject, resetter, twoFactor, totp, clientId, memberRoleId,
    organizationId: guid(organization.id), webhookKey: webhook.webhookKey, webhookKeyId: guid(webhook.id),
    resets: 0, expectedHashKey: 0, liveCookie: () => liveCookie,
  };
  const refreshLiveCookie = async () => {
    liveCookie = idpCookieOf(must(await login(subject.email, PASSWORD), [200], "live subject sign-in"));
  };

  // 1. Set A, under key 1's legacy form.
  const setA = await issueSet("A", ctx);

  // 2. Stage key 2, promote it.
  await change(admin, "Stage");
  const promoted = await change(admin, "Promote");
  if (Number(promoted.currentKeyId) !== 2 || !promoted.legacyCutoverUtc) {
    record("promote key 2", "FAIL", `current ${promoted.currentKeyId}, cutover ${promoted.legacyCutoverUtc ?? "not recorded"}`);
    return;
  }
  await refreshLiveCookie();
  ctx.expectedHashKey = 2;
  if (!judge("after promote (set A, legacy)", await present(setA, ctx), true, await webhookUse(ctx), "hk2$")) return;

  // 3. Set B under key 2; reinstate key 1.
  const setB = await issueSet("B", ctx);
  const reinstated = await change(admin, "Reinstate", 1);
  if (Number(reinstated.currentKeyId) !== 1) {
    record("reinstate key 1", "FAIL", `current ${reinstated.currentKeyId}`);
    return;
  }
  await refreshLiveCookie();
  ctx.expectedHashKey = 1;
  if (!judge("after reinstate 1 (set B, key 2)", await present(setB, ctx), true, await webhookUse(ctx), LEGACY_FORM)) return;

  // 4. Set C under key 1 again; stage key 3, promote it.
  const setC = await issueSet("C", ctx);
  await change(admin, "Stage");
  const second = await change(admin, "Promote");
  if (Number(second.currentKeyId) !== 3) {
    record("second promote (key 3)", "FAIL", `current ${second.currentKeyId}`);
    return;
  }
  await refreshLiveCookie();
  ctx.expectedHashKey = 3;
  if (!judge("after second promote (set C, key 1)", await present(setC, ctx), true, await webhookUse(ctx), "hk3$")) return;

  // 5. Set D under key 3; emergency.
  const setD = await issueSet("D", ctx);
  const emergency = await change(admin, "PromoteEmergency");
  const leaked = emergency.keys.filter((k) => k.compromised).map((k) => Number(k.id)).sort();
  if (Number(emergency.currentKeyId) !== 4 || leaked.join(",") !== "1,2,3") {
    record("emergency (key 4)", "FAIL", `current ${emergency.currentKeyId}, leaked keys ${leaked.join(",") || "none"}`);
    return;
  }
  await refreshLiveCookie();
  ctx.expectedHashKey = 4;
  judge("after emergency (set D, key 3)", await present(setD, ctx), false, await webhookUse(ctx), "hk4$");
}

try {
  await main();
} catch (error) {
  if (error instanceof Inconclusive) {
    record("run", "INCONCLUSIVE", error.message);
  } else if (error instanceof SetupError) {
    console.error(`SETUP ABORTED: ${error.message}`);
    process.exit(3);
  } else {
    console.error(error);
    process.exit(3);
  }
}

if (verdicts.length === 0) {
  console.error("No verdict was reached.");
  process.exit(3);
}
const has = (v) => verdicts.some((x) => x.verdict === v);
process.exit(has("FAIL") ? 1 : has("INCONCLUSIVE") ? 4 : 0);
