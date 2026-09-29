import type { CspExpectation } from "./csp-observer"

/**
 * THE register of CSP violations the applications raise today under their real
 * web.config policy - one place, each with its cause (card S30a, target 22).
 *
 * Every harness test TOLERATES these, so a test about something else is not
 * failed by a known, explained violation. harness-smoke.spec.ts REQUIRES them,
 * so the day one is fixed the smoke test fails with "declared but did not
 * happen" and the entry is removed here - the register cannot go stale.
 *
 * Never add an entry to make a test pass without its cause; never widen the
 * policy instead. An entry names what to change to remove it.
 */
export const KNOWN_CSP_VIOLATIONS: readonly CspExpectation[] = [
  {
    directive: "script-src",
    blocked: "eval",
    reason:
      "zod v4 probes for eval with `new Function(\"\")` inside try/catch " +
      "(zod/v4/core/util.js, allowsEval). The policy has no 'unsafe-eval', so the " +
      "probe throws, zod catches it and uses its non-JIT parser: nothing breaks, but " +
      "Chromium still reports a violation on every page that loads zod (both entry " +
      "chunks). Removed by setting zod's `jitless` config at start-up in both apps, " +
      "which skips the probe; that is product code, outside S30a.",
  },
]
