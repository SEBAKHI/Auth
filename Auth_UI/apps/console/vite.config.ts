import path from "path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@astoom/account": path.resolve(__dirname, "../../packages/account/src"),
      "@astoom/api": path.resolve(__dirname, "../../packages/api/src"),
      "@astoom/auth": path.resolve(__dirname, "../../packages/auth/src"),
      "@astoom/i18n": path.resolve(__dirname, "../../packages/i18n/src"),
      "@astoom/ui": path.resolve(__dirname, "../../packages/ui/src"),
    },
  },
})
