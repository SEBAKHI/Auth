import "@authsystem/i18n"

import { QueryClientProvider } from "@tanstack/react-query"
import { RouterProvider } from "react-router-dom"

import { Toaster } from "@authsystem/ui/sonner"
import { AppVersionMonitor } from "@authsystem/ui/common/app-version-monitor"
import { ThemeSync } from "@authsystem/ui/common/theme-sync"
import { TooltipProvider } from "@authsystem/ui/tooltip"
import { AuthProvider } from "@authsystem/auth/auth-context"
import { BrandingProvider } from "@authsystem/ui/branding"
import { DirectionProvider } from "@authsystem/i18n/direction"
import { queryClient } from "@authsystem/api/query"
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
