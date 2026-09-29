/**
 * U-2: the pnpm audit gate's decision (Tools/github/pnpm-audit-gate.mjs).
 * Run with the governance harness: node --test "Tools/github/*.test.mjs".
 *
 * Every failure case below is its own deliberate break: an evaluate() that
 * returned success for it would fail that test, and a stand-in that always
 * returns success fails all of them.
 */
import assert from "node:assert/strict";
import { describe, test } from "node:test";

import { escapeCommandData, evaluate } from "./pnpm-audit-gate.mjs";

const TODAY = "2026-09-29";

/** One advisory in the shape pnpm 11.8.0 printed for Auth_UI on 2026-09-29. */
function advisory(id, ghsa, moduleName, severity, version) {
  return {
    findings: [{ version, paths: [`.>${moduleName}`], dev: true, optional: false, bundled: false }],
    id,
    title: `${moduleName} advisory`,
    module_name: moduleName,
    vulnerable_versions: `<=${version}`,
    patched_versions: `>${version}`,
    severity,
    cwe: "CWE-400",
    github_advisory_id: ghsa,
    url: `https://github.com/advisories/${ghsa}`,
  };
}

function report(...advisories) {
  return JSON.stringify({
    advisories: Object.fromEntries(advisories.map((a) => [String(a.id), a])),
    metadata: { vulnerabilities: { info: 0, low: 0, moderate: 1, high: 1, critical: 0 }, totalDependencies: 746 },
  });
}

const JS_YAML = advisory(1121860, "GHSA-h67p-54hq-rp68", "js-yaml", "moderate", "4.1.1");
const BRACE = advisory(1123896, "GHSA-3jxr-9vmj-r5cp", "brace-expansion", "high", "2.1.1");

const entry = (ghsa, date, expires, reason = "no fixed version reachable; dev-only tool") => ({
  ghsa,
  date,
  expires,
  reason,
});

describe("U-2 pnpm audit gate: evaluate()", () => {
  test("exit 0 passes", () => {
    const verdict = evaluate({ exitCode: 0, stdout: "{}" }, [], TODAY);
    assert.equal(verdict.ok, true);
    assert.deepEqual(verdict.errors, []);
  });

  test("exit 1 with an advisory that is not allowed fails and names the GHSA and the package", () => {
    const verdict = evaluate({ exitCode: 1, stdout: report(JS_YAML) }, [], TODAY);
    assert.equal(verdict.ok, false);
    assert.equal(verdict.errors.length, 1);
    assert.match(verdict.errors[0], /GHSA-h67p-54hq-rp68 js-yaml 4\.1\.1 \(moderate\)/);
  });

  test("exit 1 fails on the advisory that is left when only some are allowed", () => {
    const allow = [entry("GHSA-h67p-54hq-rp68", "2026-09-20", "2026-12-01")];
    const verdict = evaluate({ exitCode: 1, stdout: report(JS_YAML, BRACE) }, allow, TODAY);
    assert.equal(verdict.ok, false);
    assert.deepEqual(
      verdict.errors.map((e) => e.split(" ")[0]),
      ["GHSA-3jxr-9vmj-r5cp"],
    );
  });

  test("exit 1 where every advisory is allowed and unexpired passes with one warning per advisory", () => {
    const allow = [
      entry("GHSA-h67p-54hq-rp68", "2026-09-20", "2026-12-01"),
      entry("GHSA-3jxr-9vmj-r5cp", "2026-09-29", "2026-12-28"),
    ];
    const verdict = evaluate({ exitCode: 1, stdout: report(JS_YAML, BRACE) }, allow, TODAY);
    assert.equal(verdict.ok, true);
    assert.deepEqual(verdict.errors, []);
    assert.equal(verdict.warnings.length, 2);
    assert.ok(verdict.warnings.every((w) => /is suppressed until \d{4}-\d{2}-\d{2}:/.test(w)));
  });

  test("an entry that expires today still suppresses", () => {
    const allow = [entry("GHSA-h67p-54hq-rp68", "2026-07-01", TODAY)];
    assert.equal(evaluate({ exitCode: 1, stdout: report(JS_YAML) }, allow, TODAY).ok, true);
  });

  test("an expired entry suppresses nothing and is named", () => {
    const allow = [entry("GHSA-h67p-54hq-rp68", "2026-07-01", "2026-09-28")];
    const verdict = evaluate({ exitCode: 1, stdout: report(JS_YAML) }, allow, TODAY);
    assert.equal(verdict.ok, false);
    assert.match(verdict.errors[0], /GHSA-h67p-54hq-rp68/);
    assert.ok(verdict.warnings.some((w) => /GHSA-h67p-54hq-rp68 expired on 2026-09-28/.test(w)));
  });

  test("exit 1 with no advisory at moderate or above fails (unexplained result)", () => {
    const low = advisory(1, "GHSA-aaaa-bbbb-cccc", "tiny", "low", "1.0.0");
    for (const stdout of [report(low), report()]) {
      const verdict = evaluate({ exitCode: 1, stdout }, [], TODAY);
      assert.equal(verdict.ok, false);
      assert.match(verdict.errors[0], /no advisory at moderate or above/);
    }
  });

  test("any exit code other than 0 and 1 fails closed", () => {
    for (const exitCode of [2, 137, -1, null, undefined]) {
      const verdict = evaluate({ exitCode, stdout: report(JS_YAML) }, [], TODAY);
      assert.equal(verdict.ok, false, `exit ${exitCode}`);
      assert.match(verdict.errors[0], /exited with/);
    }
  });

  test("stdout that is not JSON fails closed", () => {
    for (const stdout of ["", "ERR_PNPM_AUDIT_BAD_RESPONSE  The audit endpoint responded with 503", "{ advisories:"]) {
      const verdict = evaluate({ exitCode: 1, stdout }, [], TODAY);
      assert.equal(verdict.ok, false);
      assert.match(verdict.errors[0], /not JSON/);
    }
  });

  test("JSON without an advisories object fails closed", () => {
    // The first one is what pnpm 11.8.0 printed, with exit code 1, when the
    // registry could not be reached (Auth_UI/.npmrc registry=https://registry.invalid/).
    const registryFailure = '{\n  "error": {\n    "code": "pnpm",\n    "message": "fetch failed"\n  }\n}\n';
    for (const stdout of [registryFailure, '{"metadata":{}}', '{"advisories":[]}', '{"advisories":null}', "[]", "42", '"text"']) {
      const verdict = evaluate({ exitCode: 1, stdout }, [], TODAY);
      assert.equal(verdict.ok, false, stdout);
      assert.match(verdict.errors[0], /unrecognised report/);
    }
  });

  test("an advisory without a GHSA id or a known severity fails closed, even when another is allowed", () => {
    const noGhsa = { ...JS_YAML, github_advisory_id: undefined };
    const badGhsa = { ...JS_YAML, github_advisory_id: "CVE-2026-0001" };
    const noSeverity = { ...BRACE, severity: undefined };
    const oddSeverity = { ...BRACE, severity: "severe" };
    const numericSeverity = { ...BRACE, severity: 3 };
    const allow = [
      entry("GHSA-3jxr-9vmj-r5cp", "2026-09-20", "2026-12-01"),
      entry("GHSA-h67p-54hq-rp68", "2026-09-20", "2026-12-01"),
    ];
    for (const bad of [noGhsa, badGhsa, noSeverity, oddSeverity, numericSeverity]) {
      const verdict = evaluate({ exitCode: 1, stdout: report(JS_YAML, bad) }, allow, TODAY);
      assert.equal(verdict.ok, false);
      assert.match(verdict.errors.at(-1), /unrecognised report/);
    }
    const notAnObject = JSON.stringify({ advisories: { 7: "text" } });
    const verdict = evaluate({ exitCode: 1, stdout: notAnObject }, allow, TODAY);
    assert.equal(verdict.ok, false);
    assert.match(verdict.errors[0], /unrecognised report/);
  });

  test("an invalid allow list fails the gate even when pnpm exits 0", () => {
    const invalid = [
      "not an array",
      [entry("GHSA-h67p-54hq-rp68", "2026-09-20", "2027-03-01")],
      [entry("GHSA-h67p-54hq-rp68", "2026-09-20", "2026-12-01", "")],
      [entry("ghsa-h67p", "2026-09-20", "2026-12-01")],
    ];
    for (const list of invalid) {
      const verdict = evaluate({ exitCode: 0, stdout: "" }, list, TODAY);
      assert.equal(verdict.ok, false);
      assert.match(verdict.errors[0], /pnpm-audit-allow\.json is invalid/);
    }
  });

  test("an entry dated after today fails, so a future date cannot stretch the 90-day window", () => {
    const allow = [entry("GHSA-h67p-54hq-rp68", "2029-01-01", "2029-03-31")];
    const verdict = evaluate({ exitCode: 1, stdout: report(JS_YAML) }, allow, TODAY);
    assert.equal(verdict.ok, false);
    assert.match(verdict.errors[0], /dates GHSA-h67p-54hq-rp68 after today/);
  });

  test("an invalid today fails closed", () => {
    assert.equal(evaluate({ exitCode: 0, stdout: "" }, [], "2026-13-01").ok, false);
  });
});

describe("U-2 pnpm audit gate: workflow command output", () => {
  test("report text cannot start a new workflow command line", () => {
    assert.equal(escapeCommandData("a\n::add-mask::x\r%"), "a%0A::add-mask::x%0D%25");
  });
});
