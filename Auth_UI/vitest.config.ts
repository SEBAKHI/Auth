/// <reference types="vitest/config" />
import path from "path"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vitest/config"

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./apps/console/src"),
      "@astoom/api": path.resolve(__dirname, "./packages/api/src"),
      "@astoom/auth": path.resolve(__dirname, "./packages/auth/src"),
      "@astoom/i18n": path.resolve(__dirname, "./packages/i18n/src"),
      "@astoom/ui": path.resolve(__dirname, "./packages/ui/src"),
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
      provider: "v8",
      reporter: ["text", "html"],
      include: ["packages/*/src/**", "apps/*/src/**"],
    },
  },
})
