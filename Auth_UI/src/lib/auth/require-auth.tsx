import { Loader2 } from "lucide-react"
import { Navigate, Outlet, useLocation } from "react-router-dom"

import { useAuth } from "./auth-context"

/** Full-screen loading state shown while the session is being established. */
function FullScreenLoader() {
  return (
    <div className="flex min-h-svh items-center justify-center">
      <Loader2 className="size-6 animate-spin text-muted-foreground" />
    </div>
  )
}

/**
 * Route guard for authenticated areas. Redirects unauthenticated users to the
 * login page, preserving the attempted location for post-login return.
 */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === "loading") return <FullScreenLoader />

  if (status === "unauthenticated") {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <Outlet />
}

/** Inverse guard: keeps already-authenticated users out of auth pages. */
export function RequireAnonymous() {
  const { status } = useAuth()

  if (status === "loading") return <FullScreenLoader />
  if (status === "authenticated") return <Navigate to="/" replace />

  return <Outlet />
}
