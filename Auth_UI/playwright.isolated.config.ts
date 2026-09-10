import { defineConfig, devices } from "@playwright/test"

/**
 * Deterministic browser journeys against the production artifacts of BOTH
 * applications. API traffic is fulfilled inside each test, so this suite never
 * needs credentials and cannot mutate a shared or production database.
 *
 * Two projects, one per built app, because the two are different sites: the
 * console's specs sign in through `installAuthenticatedApi`, the accounts specs
 * under `e2e/isolated/accounts/` start signed out and walk the flows a stranger
 * takes (sign-up first among them) through `installAnonymousApi`.
 */
export default defineConfig({
  testDir: "./e2e/isolated",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  // The console's visual snapshots predate the split into two projects and are
  // named without a project suffix; keep that name so they are compared, not
  // re-recorded, and so a snapshot never differs by which project drew it.
  snapshotPathTemplate:
    "{snapshotDir}/{testFileDir}/{testFileName}-snapshots/{arg}{-snapshotSuffix}{ext}",
  use: {
    ...devices["Desktop Chrome"],
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "console-isolated",
      testDir: "./e2e/isolated",
      testIgnore: "**/accounts/**",
      use: { baseURL: "http://localhost:4175" },
    },
    {
      name: "accounts-isolated",
      testDir: "./e2e/isolated/accounts",
      use: { baseURL: "http://localhost:4176" },
    },
  ],
  webServer: [
    {
      command:
        "pnpm --filter @authsystem/console preview --host 127.0.0.1 --port 4175 --strictPort",
      url: "http://localhost:4175",
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command:
        "pnpm --filter @authsystem/accounts preview --host 127.0.0.1 --port 4176 --strictPort",
      url: "http://localhost:4176",
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
})
