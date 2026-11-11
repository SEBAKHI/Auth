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
// Empty since card S02 PR B. Its last entry was zod v4's eval probe
// (`new Function("")` in zod/v4/core/util.js, allowsEval), refused on every page
// that loaded zod; both applications now turn zod's JIT off before any schema is
// built (packages/auth/src/zod-jitless.ts), so the probe never runs. A violation
// on any page is now a failure until it is fixed or entered here with its cause.
export const KNOWN_CSP_VIOLATIONS: readonly CspExpectation[] = []
