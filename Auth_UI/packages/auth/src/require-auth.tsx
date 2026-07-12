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
  const location = useLocation()

  if (status === "loading") return <FullScreenLoader />
  if (status === "authenticated") {
    // Honor the post-login return target; this guard races the login page's
    // own navigate(from) once the session flips to authenticated, so both
    // must agree on the destination (including the query string).
    const state = location.state as {
      from?: { pathname?: string; search?: string }
    } | null
    const to = state?.from?.pathname
      ? state.from.pathname + (state.from.search ?? "")
      : "/"
    return <Navigate to={to} replace />
  }

  return <Outlet />
}
