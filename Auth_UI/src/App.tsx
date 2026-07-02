import "@/lib/i18n"

import { QueryClientProvider } from "@tanstack/react-query"
import { RouterProvider } from "react-router-dom"

import { Toaster } from "@/components/ui/sonner"
import { TooltipProvider } from "@/components/ui/tooltip"
import { AuthProvider } from "@/lib/auth/auth-context"
import { DirectionProvider } from "@/lib/i18n/direction"
import { queryClient } from "@/lib/query"
import { router } from "@/routes"

export function App() {
  return (
    <DirectionProvider>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <TooltipProvider delayDuration={300}>
            <RouterProvider router={router} />
          </TooltipProvider>
          <Toaster position="top-center" richColors />
        </AuthProvider>
      </QueryClientProvider>
    </DirectionProvider>
  )
}

export default App
