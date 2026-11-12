import { config } from "zod"

/**
 * Turns zod's JIT parser off before any schema is built (card S02 PR B; OI-03).
 *
 * zod v4 compiles object parsers with `new Function`, and first probes whether
 * the page allows it (`allowsEval`, zod/v4/core/util.js). Both applications'
 * CSP has no 'unsafe-eval', so the probe throws and zod falls back to its
 * plain parser - nothing breaks - but the browser still reports a script-src
 * violation on every page that loads zod. Under `jitless` zod skips the probe
 * and the JIT, so a strict policy sees no eval at all, and any eval violation
 * the browser harness sees is a real one (harness-smoke.spec.ts fails on it).
 *
 * The probe runs when the first object schema is built, and a module-level
 * schema is built when its module is evaluated: each application's main.tsx
 * imports this module before any other module that uses zod
 * (zod-jitless.test.ts holds that). The setting lives in zod's global config,
 * so it applies to every copy of zod in the bundle that reads it.
 */
config({ jitless: true })
