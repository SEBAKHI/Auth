import path from "path"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vitest/config"

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./apps/console/src"),
      "@authsystem/account": path.resolve(__dirname, "./packages/account/src"),
      "@authsystem/api": path.resolve(__dirname, "./packages/api/src"),
      "@authsystem/auth": path.resolve(__dirname, "./packages/auth/src"),
      "@authsystem/i18n": path.resolve(__dirname, "./packages/i18n/src"),
      "@authsystem/ui": path.resolve(__dirname, "./packages/ui/src"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./test/setup.ts"],
    css: false,
    include: [
      "apps/*/src/**/*.{test,spec}.{ts,tsx}",
      "packages/*/src/**/*.{test,spec}.{ts,tsx}",
    ],
    coverage: {
      // Istanbul instruments source files that no test imports. V8 emitted
      // empty maps for those files here, which excluded real zero-coverage
      // modules from the denominator and made the repository total unreliable.
      provider: "istanbul",
      reporter: ["text", "html", "json", "json-summary"],
      // Explicit source-file patterns keep declaration and test defaults out
      // while ensuring both applications and every shared package are counted.
      include: ["packages/*/src/**/*.{ts,tsx}", "apps/*/src/**/*.{ts,tsx}"],
      // Truthful all-source baseline after migrating from V8's imported-file
      // denominator to Istanbul. This ratchet may only move upward; changed
      // production lines are measured separately by verify-changed-coverage.mjs.
      // Left far below what the suite achieved, a floor stops protecting
      // anything: coverage could fall twenty points and still pass. These sit
      // just under the current run.
      thresholds: {
        statements: 56,
        branches: 45.1,
        functions: 48.9,
        lines: 56.9,
      },
    },
  },
})
