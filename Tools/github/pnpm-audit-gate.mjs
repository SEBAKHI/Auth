/**
 * The pnpm half of the "Dependency audit" workflow (job "pnpm audit (default
 * branch)", .github/workflows/dependency-audit.yml): runs `pnpm audit` over
 * Auth_UI/pnpm-lock.yaml and applies Tools/github/pnpm-audit-allow.json, a list
 * of dated, reviewed, expiring GHSA entries.
 *
 * Why a wrapper instead of plain `pnpm audit`: an absolute audit turns red on
 * any advisory published against a version nobody touched. Without a reviewed
 * way back to green it stays red every day, and a check that is always red is a
 * check people learn to ignore. pnpm's own routes are closed on purpose: audit
 * keys in pnpm-workspace.yaml are forbidden (guard G-S03d), and --ignore /
 * --ignore-unfixable / --fix write configuration and exit 0.
 *
 * Decision (evaluate, below), failing CLOSED on anything it does not recognise:
 *   - pnpm exit 0: pass only with a clean report: JSON with an advisories
 *     object holding no moderate/high/critical entry, and metadata that audited
 *     something (totalDependencies > 0) and counts no moderate/high/critical.
 *     An exit code alone proves nothing: with ignoreRegistryErrors set, pnpm
 *     exits 0 and prints "fetch failed";
 *   - pnpm exit 1: every moderate/high/critical advisory in the JSON report must
 *     match an unexpired allow-list entry by GHSA; each suppressed one is printed
 *     as a warning, anything left fails;
 *   - exit 1 with no such advisory, any other exit code, output that is not
 *     JSON, a report without an advisories object, or an advisory without a
 *     GHSA id or a known severity: fail.
 *
 * The report's shape comes from what pnpm 11.8.0 actually printed for this
 * lockfile on 2026-09-29. With advisories (exit 1): { advisories: { <id>: {
 * github_advisory_id, severity, module_name, findings: [{ version }] } },
 * metadata }. Clean (exit 0; measured with --audit-level critical, which left
 * none at its level): { "advisories": {}, "metadata": { "vulnerabilities":
 * { info, low, moderate, high, critical }, "dependencies": 207,
 * "devDependencies": 495, "optionalDependencies": 49, "totalDependencies": 746 } };
 * the metadata counts are not filtered by --audit-level. No vendor document
 * names these fields, so a pnpm upgrade that renames them turns this job red
 * ("unrecognised report") rather than green.
 *
 * Usage (CI runs it from Auth_UI; Linux or macOS, because pnpm is started
 * without a shell and Windows ships pnpm as a .cmd file):
 *   cd Auth_UI && node ../Tools/github/pnpm-audit-gate.mjs
 * Zero npm dependencies: node: built-ins only.
 */
import { spawnSync } from "node:child_process";
import { readFileSync, realpathSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Fixed arguments, never taken from the environment. Guard G-S03i
 * (governance.test.mjs) imports this array and forbids --ignore-registry-errors
 * (exit 0 on a registry failure), --fix / --ignore / --ignore-unfixable /
 * --interactive (write configuration, exit 0) and --registry.
 * --audit-level is explicit so a workspace setting cannot weaken it,
 * --fail-if-no-match stops a silent pass when no workspace project is found,
 * and --ignore-pnpmfile keeps a repository .pnpmfile from running inside it.
 */
export const PNPM_AUDIT_ARGS = Object.freeze([
  "audit",
  "--json",
  "--audit-level",
  "moderate",
  "--fail-if-no-match",
  "--ignore-pnpmfile",
]);

/** GitHub advisory identifier, e.g. GHSA-h67p-54hq-rp68. */
export const GHSA_PATTERN = /^GHSA-[0-9a-z]{4}-[0-9a-z]{4}-[0-9a-z]{4}$/;

/** The longest an allow-list entry may live, in days after its date. */
export const MAX_ALLOW_DAYS = 90;

const KNOWN_SEVERITIES = new Set(["info", "low", "moderate", "high", "critical"]);
const FAILING_SEVERITIES = new Set(["moderate", "high", "critical"]);
const ENTRY_KEYS = ["ghsa", "date", "expires", "reason"];
const DAY_MS = 24 * 60 * 60 * 1000;

const isPlainObject = (value) =>
  value !== null && typeof value === "object" && !Array.isArray(value);

/** Parses a YYYY-MM-DD calendar date to UTC midnight; null for anything else. */
export function parseIsoDate(text) {
  if (typeof text !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(text)) return null;
  const time = Date.parse(`${text}T00:00:00Z`);
  // Date.parse rolls 2026-02-30 over to March; a real date prints back unchanged.
  if (Number.isNaN(time) || new Date(time).toISOString().slice(0, 10) !== text) return null;
  return time;
}

/**
 * Checks a date window: the expiry is after the date and at most
 * MAX_ALLOW_DAYS later. Static on purpose: it never reads today's date, so
 * the required governance check cannot turn red as time passes.
 * Returns a problem description, or null when the window is valid.
 */
export function checkAllowWindow(date, expires) {
  const start = parseIsoDate(date);
  const end = parseIsoDate(expires);
  if (start === null) return `date "${date}" is not a YYYY-MM-DD calendar date`;
  if (end === null) return `expiry "${expires}" is not a YYYY-MM-DD calendar date`;
  if (end <= start) return `expiry ${expires} is not after the date ${date}`;
  if (end - start > MAX_ALLOW_DAYS * DAY_MS)
    return `expiry ${expires} is more than ${MAX_ALLOW_DAYS} days after the date ${date}`;
  return null;
}

/**
 * Static validation of Tools/github/pnpm-audit-allow.json (guard G-S03k, and
 * the gate itself before it trusts the list). Returns violation messages.
 */
export function validateAllowList(value) {
  if (!Array.isArray(value)) return ["the allow list is not a JSON array"];
  const problems = [];
  const seen = new Set();
  value.forEach((entry, index) => {
    const at = `entry ${index}`;
    if (!isPlainObject(entry)) {
      problems.push(`${at} is not an object`);
      return;
    }
    const extra = Object.keys(entry).filter((key) => !ENTRY_KEYS.includes(key));
    if (extra.length > 0) problems.push(`${at} has unknown keys: ${extra.join(", ")}`);
    if (typeof entry.ghsa !== "string" || !GHSA_PATTERN.test(entry.ghsa)) {
      problems.push(`${at}: "ghsa" is not a GHSA-xxxx-xxxx-xxxx identifier`);
    } else if (seen.has(entry.ghsa)) {
      problems.push(`${at}: ${entry.ghsa} is listed more than once`);
    } else {
      seen.add(entry.ghsa);
    }
    const window = checkAllowWindow(entry.date, entry.expires);
    if (window !== null) problems.push(`${at}: ${window}`);
    if (typeof entry.reason !== "string" || entry.reason.trim() === "")
      problems.push(`${at}: "reason" is missing or empty`);
  });
  return problems;
}

/** Escapes data for a GitHub workflow command, so report text cannot inject one. */
export function escapeCommandData(text) {
  return String(text).replace(/%/g, "%25").replace(/\r/g, "%0D").replace(/\n/g, "%0A");
}

/**
 * The gate's decision. Pure: the pnpm result, the parsed allow list and
 * today's UTC date (YYYY-MM-DD) in, a verdict out.
 * @returns {{ ok: boolean, errors: string[], warnings: string[] }}
 */
export function evaluate({ exitCode, stdout }, allowList, today) {
  const errors = [];
  const warnings = [];
  const fail = (message) => ({ ok: false, errors: [...errors, message], warnings });

  const listProblems = validateAllowList(allowList);
  if (listProblems.length > 0)
    return fail(`Tools/github/pnpm-audit-allow.json is invalid: ${listProblems.join("; ")}`);
  if (parseIsoDate(today) === null) return fail(`today "${today}" is not a YYYY-MM-DD date`);

  // A date in the future would stretch the 90-day window from today; this
  // check reads today's date, which is safe here because the job is not required.
  const future = allowList.filter((entry) => entry.date > today).map((entry) => entry.ghsa);
  if (future.length > 0)
    return fail(`Tools/github/pnpm-audit-allow.json dates ${future.join(", ")} after today (${today}); date an entry on the day it is accepted`);

  const active = new Map();
  for (const entry of allowList) {
    if (entry.expires >= today) active.set(entry.ghsa, entry);
    else
      warnings.push(
        `allow-list entry ${entry.ghsa} expired on ${entry.expires} and suppresses nothing; renew it in a pull request with a new date and reason, or upgrade the package`,
      );
  }

  if (exitCode === 0) {
    let clean;
    try {
      clean = JSON.parse(stdout);
    } catch {
      return fail("pnpm audit exited with 0 but printed output that is not JSON (unrecognised report)");
    }
    const counts = isPlainObject(clean?.metadata) ? clean.metadata.vulnerabilities : undefined;
    const recognised =
      isPlainObject(clean) &&
      isPlainObject(clean.advisories) &&
      Object.values(clean.advisories).every((a) => isPlainObject(a) && !FAILING_SEVERITIES.has(a.severity)) &&
      isPlainObject(clean.metadata) &&
      Number.isInteger(clean.metadata.totalDependencies) &&
      clean.metadata.totalDependencies > 0 &&
      (counts === undefined ||
        (isPlainObject(counts) && [...FAILING_SEVERITIES].every((severity) => (counts[severity] ?? 0) === 0)));
    if (!recognised)
      return fail("unrecognised report: pnpm audit exited with 0 without a clean report (advisories object, nothing at moderate or above, totalDependencies > 0)");
    return { ok: true, errors, warnings };
  }
  if (exitCode !== 1)
    return fail(`pnpm audit exited with ${exitCode ?? "no exit code"}; only 0 and 1 have a meaning, so the audit is treated as failed`);

  let report;
  try {
    report = JSON.parse(stdout);
  } catch {
    return fail("pnpm audit printed output that is not JSON (unrecognised report)");
  }
  if (!isPlainObject(report) || !isPlainObject(report.advisories))
    return fail("unrecognised report: the JSON has no advisories object");

  const findings = [];
  for (const [key, advisory] of Object.entries(report.advisories)) {
    if (!isPlainObject(advisory)) return fail(`unrecognised report: advisory ${key} is not an object`);
    const ghsa = advisory.github_advisory_id;
    if (typeof ghsa !== "string" || !GHSA_PATTERN.test(ghsa))
      return fail(`unrecognised report: advisory ${key} has no GHSA identifier`);
    if (!KNOWN_SEVERITIES.has(advisory.severity))
      return fail(`unrecognised report: advisory ${ghsa} has no known severity`);
    if (!FAILING_SEVERITIES.has(advisory.severity)) continue;
    const versions = Array.isArray(advisory.findings)
      ? [...new Set(advisory.findings.map((finding) => finding?.version).filter((v) => typeof v === "string"))]
      : [];
    findings.push({
      ghsa,
      severity: advisory.severity,
      module: typeof advisory.module_name === "string" ? advisory.module_name : "(unnamed package)",
      versions,
    });
  }
  if (findings.length === 0)
    return fail("pnpm audit exited with 1 but its report lists no advisory at moderate or above (unexplained result)");

  for (const finding of findings) {
    const label = `${finding.ghsa} ${finding.module}${finding.versions.length ? ` ${finding.versions.join(", ")}` : ""} (${finding.severity})`;
    const entry = active.get(finding.ghsa);
    if (entry) warnings.push(`${label} is suppressed until ${entry.expires}: ${entry.reason}`);
    else errors.push(`${label} is not in Tools/github/pnpm-audit-allow.json`);
  }
  return { ok: errors.length === 0, errors, warnings };
}

function main() {
  const allowPath = fileURLToPath(new URL("./pnpm-audit-allow.json", import.meta.url));
  let allowList;
  try {
    allowList = JSON.parse(readFileSync(allowPath, "utf8").replace(/^﻿/, ""));
  } catch (error) {
    console.log(`::error::Cannot read Tools/github/pnpm-audit-allow.json: ${escapeCommandData(error.message)}`);
    process.exitCode = 1;
    return;
  }

  const run = spawnSync("pnpm", PNPM_AUDIT_ARGS, {
    encoding: "utf8",
    shell: false,
    stdio: ["ignore", "pipe", "inherit"],
    maxBuffer: 256 * 1024 * 1024,
  });
  if (run.error) {
    console.log(`::error::Could not run pnpm audit: ${escapeCommandData(run.error.message)}`);
    process.exitCode = 1;
    return;
  }

  const today = new Date().toISOString().slice(0, 10);
  const verdict = evaluate({ exitCode: run.status, stdout: run.stdout }, allowList, today);
  for (const warning of verdict.warnings) console.log(`::warning::${escapeCommandData(warning)}`);
  for (const error of verdict.errors) console.log(`::error::${escapeCommandData(error)}`);
  console.log(
    verdict.ok
      ? "pnpm audit gate: pass."
      : "pnpm audit gate: FAIL. Triage per Tools/github/README.md (upgrade, override, or a dated allow-list entry).",
  );
  process.exitCode = verdict.ok ? 0 : 1;
}

// Run main() only when executed, not when imported by the tests. Both paths are
// resolved through symlinks and junctions: comparing an unresolved argv path
// with the module's real path would skip main() and exit 0 in silence.
function realPath(path) {
  try {
    return realpathSync(path);
  } catch {
    return resolve(path);
  }
}
const invokedPath = process.argv[1] ? realPath(process.argv[1]) : "";
const modulePath = realPath(fileURLToPath(import.meta.url));
const sameFile =
  process.platform === "win32"
    ? invokedPath.toLowerCase() === modulePath.toLowerCase()
    : invokedPath === modulePath;
if (sameFile) main();
