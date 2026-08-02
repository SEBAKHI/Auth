import { defineConfig, devices } from "@playwright/test"

/**
 * Runs browser checks against the exact Vite production artifacts. This keeps
 * production-only bundling, routing, and stale-shell regressions visible.
 */
export default defineConfig({
  testDir: "./e2e/production",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  use: {
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "console-production",
      use: { ...devices["Desktop Chrome"], baseURL: "http://localhost:4173" },
    },
    {
      name: "accounts-production",
      use: { ...devices["Desktop Chrome"], baseURL: "http://localhost:4174" },
    },
  ],
  webServer: [
    {
      command:
        "pnpm --filter @authsystem/console preview --host 127.0.0.1 --port 4173 --strictPort",
      url: "http://localhost:4173",
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command:
        "pnpm --filter @authsystem/accounts preview --host 127.0.0.1 --port 4174 --strictPort",
      url: "http://localhost:4174",
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
})
