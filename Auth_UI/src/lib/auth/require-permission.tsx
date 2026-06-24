import * as React from "react"
import { Navigate, Outlet } from "react-router-dom"

import { useAuth } from "./auth-context"

/**
 * Render-gate: shows its children only when the user holds the permission.
 * Use for action buttons, menu items, and inline controls.
 */
export function RequirePermission({
  permission,
  children,
  fallback = null,
}: {
  permission: string
  children: React.ReactNode
  fallback?: React.ReactNode
}) {
  const { hasPermission } = useAuth()
  return <>{hasPermission(permission) ? children : fallback}</>
}

/**
 * Route-level gate: redirects to the access-denied page when the user lacks the
 * permission. The API still enforces authorization independently.
 */
export function PermissionRoute({ permission }: { permission: string }) {
  const { hasPermission } = useAuth()
  if (!hasPermission(permission)) {
    return <Navigate to="/403" replace />
  }
  return <Outlet />
}
