import { defineConfig, devices } from "@playwright/test"

/**
 * The browser harness: both applications built into dist-harness
 * (scripts/build-harness.mjs), served with the headers their web.config makes
 * IIS send, on three same-site HTTPS origins, with a real HTTPS API host and two
 * attacker origins - all inside the Playwright worker (e2e/harness/fixtures.ts).
 * No webServer: the fixtures start everything. No route interception anywhere.
 *
 * Run it with `pnpm e2e:harness`, which builds first.
 */
export default defineConfig({
  testDir: "./e2e/harness",
  // Explicit: Playwright's default testMatch also collects *.test.ts, and
  // web-config-model.test.ts in the same folder belongs to vitest.
  testMatch: "**/*.spec.ts",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  // No retries: a harness self-test that passes on the second try is a harness
  // that cannot be trusted on the first.
  retries: 0,
  reporter: "list",
  use: {
    ...devices["Desktop Chrome"],
    // No `channel`: Playwright's own headless Chromium build, pinned by the
    // lockfile - the binary the spike proved the harness on. The full "chromium"
    // channel makes its own background requests (www.google.com, autofill), which
    // the proxy would rightly refuse and fail as egress.
    trace: "retain-on-failure",
  },
  projects: [{ name: "harness" }],
})
