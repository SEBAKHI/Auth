import "@astoom/i18n"

import { QueryClientProvider } from "@tanstack/react-query"
import { RouterProvider } from "react-router-dom"

import { Toaster } from "@astoom/ui/sonner"
import { AppVersionMonitor } from "@astoom/ui/common/app-version-monitor"
import { ThemeSync } from "@astoom/ui/common/theme-sync"
import { TooltipProvider } from "@astoom/ui/tooltip"
import { AuthProvider } from "@astoom/auth/auth-context"
import { BrandingProvider } from "@astoom/ui/branding"
import { DirectionProvider } from "@astoom/i18n/direction"
import { queryClient } from "@astoom/api/query"
import { router } from "@/routes"

export function App() {
  return (
    <DirectionProvider>
      <AppVersionMonitor />
      <QueryClientProvider client={queryClient}>
        <BrandingProvider>
          <AuthProvider>
            <ThemeSync />
            <TooltipProvider delayDuration={300}>
              <RouterProvider router={router} />
            </TooltipProvider>
            <Toaster position="top-center" richColors />
          </AuthProvider>
        </BrandingProvider>
      </QueryClientProvider>
    </DirectionProvider>
  )
}

export default App
