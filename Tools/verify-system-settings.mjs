/**
 * Live proof that settings saved through the console change real API
 * behaviour — one probe per apply mechanism, so a regression in any of them
 * shows up as a failed probe instead of a silent fall-back to appsettings.json.
 *
 * Two automated tests cover the rest: SystemSettingsApplyCoverageTests proves
 * every editable field reaches configuration and that no hot settings type is
 * still consumed through a startup-frozen IOptions<T>.
 *
 * Usage (dev API on http://localhost:5100, Development environment):
 *   node Tools/verify-system-settings.mjs
 *
 * Every probe restores what it changed; overrides are reset on exit.
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

const API = "http://localhost:5100";
const LOGS = String.raw`D:\01 - Companies\00 - Astoom\Repos\AuthSystem\Auth\Auth_API\Logs`;
const ADMIN = { email: "admin@company.com", password: "Admin@123!" };

let token = "";
const results = [];
const touched = new Set();

const record = (name, mechanism, passed, detail) => {
  results.push({ name, mechanism, passed, detail });
  console.log(`${passed ? "PASS" : "FAIL"}  ${name.padEnd(34)} ${detail}`);
};

async function call(path, { method = "GET", body, headers = {}, auth = true } = {}) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: {
      "content-type": "application/json",
      ...(auth && token ? { authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, text, headers: response.headers };
}

async function login() {
  const r = await call("/api/v1/Auth/login", { method: "POST", auth: false, body: ADMIN });
  if (r.status !== 200) throw new Error(`login failed: ${r.status} ${r.text.slice(0, 300)}`);
  // LoginResponse nests the pair under "token".
  return r.json.token?.accessToken ?? r.json.accessToken;
}

async function sections() {
  const r = await call("/api/v1/admin/system-settings");
  if (r.status !== 200) throw new Error(`settings read failed: ${r.status} ${r.text.slice(0, 300)}`);
  return Object.fromEntries(r.json.sections.map((s) => [s.key, s]));
}

async function saveSection(key, overrides) {
  const current = (await sections())[key];
  const r = await call(`/api/v1/admin/system-settings/${key}`, {
    method: "PUT",
    body: { overrides, rowVersion: current.rowVersion ?? null },
  });
  if (r.status !== 200) throw new Error(`save ${key} failed: ${r.status} ${r.text.slice(0, 400)}`);
  touched.add(key);
  return r.json;
}

const resetSection = (key) => call(`/api/v1/admin/system-settings/${key}/reset`, { method: "POST" });

const fieldOf = (section, path) => section.fields.find((f) => f.path === path);
const jwtLifetimeSeconds = (jwt) => {
  const payload = JSON.parse(Buffer.from(jwt.split(".")[1], "base64url").toString());
  return payload.exp - payload.iat;
};

function newestLog() {
  const files = readdirSync(LOGS)
    .map((f) => join(LOGS, f))
    .filter((f) => f.endsWith(".log"))
    .sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs);
  return files.length ? readFileSync(files[0], "utf8") : "";
}

// ── Probe 1: IOptionsMonitor-backed singleton (JwtTokenService) ───────────
async function probeJwtLifetime() {
  const before = jwtLifetimeSeconds(token);
  await saveSection("Jwt", { AccessTokenLifetimeMinutes: 45 });
  const after = jwtLifetimeSeconds(await login());
  record(
    "Jwt access-token lifetime",
    "IOptionsMonitor (singleton)",
    before !== 2700 && after === 2700,
    `token lifetime ${before}s → ${after}s (expected 2700s)`
  );
  await resetSection("Jwt");
  token = await login();
}

// ── Probe 2: IOptionsSnapshot-backed scoped consumer (PasswordValidator) ──
async function probePasswordPolicy() {
  // A too-short password always fails, so no account is ever created; the
  // rejection message quotes the CONFIGURED minimum, which is the proof.
  const attempt = () =>
    call("/api/v1/Auth/register", {
      method: "POST",
      auth: false,
      body: {
        email: `probe-${Date.now()}@example.com`,
        password: "Ab1!x", // 5 chars: below any allowed minimum
        firstName: "Probe",
        lastName: "Probe",
      },
    });

  const quoted = (r) => (r.json?.detail ?? "").match(/(\d+)/)?.[1];

  const before = await attempt();
  await saveSection("Password", { MinimumLength: 24 });
  const after = await attempt();

  record(
    "Password minimum length",
    "IOptionsSnapshot (scoped)",
    quoted(before) === "8" && quoted(after) === "24",
    `rejection quotes minimum: ${quoted(before) ?? "?"} → ${quoted(after) ?? "?"} (expected 8 → 24)`
  );
  await resetSection("Password");
}

// ── Probe 3: custom ICorsPolicyProvider reading live configuration ────────
async function probeCors() {
  const origin = "https://probe.example.com";
  const preflight = () =>
    call("/api/v1/Platform/branding", {
      method: "OPTIONS",
      auth: false,
      headers: { origin, "access-control-request-method": "GET" },
    });

  const before = (await preflight()).headers.get("access-control-allow-origin");
  const current = (await sections())["Cors"];
  const origins = fieldOf(current, "AllowedOrigins").effectiveValue ?? [];
  await saveSection("Cors", { AllowedOrigins: [...origins, origin] });
  const after = (await preflight()).headers.get("access-control-allow-origin");

  record(
    "CORS allowed origins",
    "dynamic policy provider",
    before !== origin && after === origin,
    `preflight allow-origin: ${before ?? "<none>"} → ${after ?? "<none>"}`
  );
  await resetSection("Cors");
}

// ── Probe 4: version-stamped rate-limiter partitions ──────────────────────
async function probeRateLimit() {
  const badLogin = () =>
    call("/api/v1/Auth/login", {
      method: "POST",
      auth: false,
      body: { email: "nobody-probe@example.com", password: "WrongPassword1!" },
    });

  await saveSection("RateLimiting", { LoginPermitLimit: 1, LoginWindowSeconds: 60 });
  const first = await badLogin();
  const second = await badLogin();

  record(
    "Rate limit (login permits)",
    "version-stamped partitions",
    second.status === 429,
    `limit 1/60s → attempt statuses ${first.status}, ${second.status} (expected 2nd = 429)`
  );
  await resetSection("RateLimiting"); // version bump frees the partition
}

// ── Probe 5: IOptionsMonitor in the SMTP transport + test endpoint ────────
async function probeEmail() {
  const test = () => call("/api/v1/admin/system-settings/email/test", { method: "POST" });
  const codeOf = (r) => r.json?.errors?.[0]?.code ?? r.json?.title ?? String(r.status);

  const before = await test();
  await saveSection("Email", { Enabled: true });
  const after = await test();

  record(
    "Email enabled toggle",
    "IOptionsMonitor (singleton)",
    codeOf(before) !== codeOf(after),
    `test-email outcome: ${before.status}/${codeOf(before)} → ${after.status}/${codeOf(after)}`
  );
  await resetSection("Email");
}

// ── Probe 6: Serilog LoggingLevelSwitch ──────────────────────────────────
async function probeSerilogLevel() {
  const marker = "SystemSettingsUpdatedEvent"; // logged at Debug by the audit handler
  const count = () => (newestLog().match(new RegExp(marker, "g")) ?? []).length;
  const settle = () => new Promise((resolve) => setTimeout(resolve, 1500));

  // Differential within this run, so a log file carrying older lines cannot
  // colour the result: identical work at Information, then at Debug.
  await resetSection("Serilog");
  await settle();
  const start = count();

  await saveSection("Jwt", { AccessTokenLifetimeMinutes: 16 });
  await settle();
  const atInformation = count();

  await saveSection("Serilog", { MinimumLevel: { Default: "Debug" } });
  await saveSection("Jwt", { AccessTokenLifetimeMinutes: 17 });
  await settle();
  const atDebug = count();

  record(
    "Serilog minimum level",
    "LoggingLevelSwitch",
    atInformation === start && atDebug > atInformation,
    `same save logged: ${start}→${atInformation} at Information, →${atDebug} at Debug`
  );
  await resetSection("Jwt");
  await resetSection("Serilog");
}

async function main() {
  token = await login();
  console.log("logged in; running probes\n");

  for (const probe of [probeJwtLifetime, probePasswordPolicy, probeCors, probeRateLimit, probeEmail, probeSerilogLevel]) {
    try {
      await probe();
    } catch (error) {
      record(probe.name, "-", false, `threw: ${error.message}`);
    }
  }

  for (const key of touched) await resetSection(key).catch(() => {});

  const failed = results.filter((r) => !r.passed);
  console.log(`\n${results.length - failed.length}/${results.length} probes passed`);
  process.exit(failed.length === 0 ? 0 : 1);
}

main().catch((error) => {
  console.error("probe run failed:", error.message);
  process.exit(1);
});
