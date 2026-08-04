import path from "path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

import { devHttps } from "../../dev-https"
import { vendorChunk } from "../../vendor-chunks"

// https://vite.dev/config/
export default defineConfig((config) => ({
  plugins: [react(), tailwindcss()],
  server: {
    // Pinned rather than left to the default: accounts owns 5174 and the API's
    // CORS allow-list names both ports, so a silent fallback port fails CORS.
    port: 5173,
    strictPort: true,
    // https so the browser keeps the IdP session cookie — see dev-https.ts.
    https: devHttps(config, __dirname),
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@authsystem/account": path.resolve(__dirname, "../../packages/account/src"),
      "@authsystem/api": path.resolve(__dirname, "../../packages/api/src"),
      "@authsystem/auth": path.resolve(__dirname, "../../packages/auth/src"),
      "@authsystem/i18n": path.resolve(__dirname, "../../packages/i18n/src"),
      "@authsystem/ui": path.resolve(__dirname, "../../packages/ui/src"),
    },
  },
  build: {
    // Fail loudly if the entry chunk creeps back up. The app shipped as a single
    // 2.5 MB chunk before route splitting, so a silent 500 kB warning was useless.
    chunkSizeWarningLimit: 400,
    rollupOptions: {
      output: {
        // Split the heavy vendors into their own chunks so a route that does not
        // use them never pays for them: recharts is only the dashboard, CodeMirror
        // only the notification editors.
        manualChunks: vendorChunk,
      },
    },
  },
}))
