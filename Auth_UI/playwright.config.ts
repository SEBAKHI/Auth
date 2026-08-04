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
    // The Auth API and both dev servers run on the local dev certificate;
    // browser navigation and XHR must not reject it.
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: "console",
      testDir: "./e2e/console",
      use: { ...devices["Desktop Chrome"], baseURL: "https://localhost:5173" },
    },
    {
      name: "accounts",
      testDir: "./e2e/accounts",
      use: { ...devices["Desktop Chrome"], baseURL: "https://localhost:5174" },
    },
  ],
  // https because the dev servers do (Auth_UI/dev-https.ts). Without
  // DEV_HTTPS_CERT/DEV_HTTPS_KEY they fall back to http and these URLs never
  // come up — the failure names the missing variables.
  webServer: [
    {
      command: "pnpm --filter @authsystem/console dev",
      url: "https://localhost:5173",
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: "pnpm --filter @authsystem/accounts dev",
      url: "https://localhost:5174",
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
})
