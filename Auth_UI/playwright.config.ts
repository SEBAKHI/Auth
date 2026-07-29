import { defineConfig, devices } from "@playwright/test"

/**
 * Playwright e2e config. Boots both Vite dev servers and runs each app's
 * browser tests against its own origin.
 * Install browsers once with: pnpm exec playwright install
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  use: {
    trace: "on-first-retry",
    // The local Auth API runs on the self-signed dev certificate
    // (https://localhost:5101); browser XHR must not reject it.
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: "console",
      testDir: "./e2e/console",
      use: { ...devices["Desktop Chrome"], baseURL: "http://localhost:5173" },
    },
    {
      name: "accounts",
      testDir: "./e2e/accounts",
      use: { ...devices["Desktop Chrome"], baseURL: "http://localhost:5174" },
    },
  ],
  webServer: [
    {
      command: "pnpm --filter @astoom/console dev",
      url: "http://localhost:5173",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: "pnpm --filter @astoom/accounts dev",
      url: "http://localhost:5174",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
})
