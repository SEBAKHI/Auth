import { Navigate, Outlet, useLocation } from "react-router-dom"

import { Spinner } from "@authsystem/ui/spinner"

import { useAuth } from "./auth-context"
import { getValidReturnTo } from "./return-to"

/** Full-screen loading state shown while the session is being established. */
function FullScreenLoader() {
  return (
    <div className="flex min-h-svh items-center justify-center">
      <Spinner className="size-6 text-muted-foreground" />
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
    // A pending authorize request outranks the guard, and the page must be
    // SHOWN rather than resumed. Being authenticated here is not the same as
    // holding a valid IdP session — the two have separate lifetimes — and the
    // authorize endpoint only bounced the browser back because it found no
    // usable one. Redirecting to the pending request would therefore be sent
    // straight back here, forever; rendering the form lets the interactive
    // sign-in mint the session that ends the flow. The same is true of a
    // step-up demand, where re-authenticating is the entire point.
    if (getValidReturnTo(location.search)) return <Outlet />


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
