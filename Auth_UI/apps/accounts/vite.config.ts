import path from "path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

import { apiPreconnect } from "../../api-preconnect"
import { devHttps } from "../../dev-https"
import { assetsCors, htmlInputs } from "../../login-design-frame"
import { themeBoot } from "../../theme-boot"
import { vendorChunk } from "../../vendor-chunks"

// https://vite.dev/config/
export default defineConfig((config) => ({
  // themeBoot: the colour mode on <html> before the first paint (theme-boot.ts).
  // apiPreconnect: the connection to the API opens while the scripts download.
  plugins: [themeBoot({ hints: true }), react(), tailwindcss(), assetsCors(), apiPreconnect()],
  server: {
    // The console dev server owns 5173; accounts runs alongside it.
    port: 5174,
    strictPort: true,
    // https so the browser keeps the IdP session cookie — see dev-https.ts.
    https: devHttps(config, __dirname),
    // Development can still inspect the API-rendered artifact without copying
    // files into Vite's root. Production serves the same rendered bytes from
    // the persistent /privacy virtual directory instead of proxying this path.
    proxy: {
      "/privacy": {
        target: "https://localhost:5101",
        changeOrigin: true,
        secure: false,
      },
    },
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
    chunkSizeWarningLimit: 400,
    rollupOptions: {
      // The frame of a sign-in page design is a second page (login-design-frame.ts).
      input: htmlInputs(__dirname),
      output: {
        // The accounts app has no charts or editors; only the framework vendors
        // are worth hoisting out of the per-route chunks.
        manualChunks: vendorChunk,
      },
    },
  },
}))
