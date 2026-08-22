import { defineConfig, devices } from "@playwright/test"

/**
 * Deterministic protected-screen journeys against the production console
 * artifact. API traffic is fulfilled inside each test, so this suite never
 * needs credentials and cannot mutate a shared or production database.
 */
export default defineConfig({
  testDir: "./e2e/isolated",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: "http://localhost:4175",
    trace: "on-first-retry",
  },
  webServer: {
    command:
      "pnpm --filter @authsystem/console preview --host 127.0.0.1 --port 4175 --strictPort",
    url: "http://localhost:4175",
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
