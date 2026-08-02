import * as React from "react"

import { useAuth } from "@authsystem/auth/auth-context"
import { isTheme, useTheme } from "@authsystem/ui/theme-provider"

/**
 * Adopts the profile's stored theme once per authenticated session — the
 * theme counterpart of the language adoption in the auth context (which
 * cannot own this: packages/auth must not depend on packages/ui). The
 * once-per-session guard keeps a mid-session manual change from being
 * overwritten by stale server state; it resets on logout.
 */
export function ThemeSync() {
  const { status, user } = useAuth()
  const { setTheme } = useTheme()
  const adoptedRef = React.useRef(false)

  const profileTheme = user?.theme

  React.useEffect(() => {
    if (status !== "authenticated") {
      adoptedRef.current = false
      return
    }

    if (adoptedRef.current) {
      return
    }
    adoptedRef.current = true

    if (profileTheme && isTheme(profileTheme)) {
      setTheme(profileTheme)
    }
  }, [status, profileTheme, setTheme])

  return null
}
