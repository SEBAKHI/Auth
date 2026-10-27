import * as React from "react"

import { useAuth } from "@authsystem/auth/auth-context"
import { readFlowHints } from "@authsystem/i18n/flow-hints"
import { isTheme, useTheme } from "@authsystem/ui/theme-provider"

/**
 * Adopts the profile's stored theme once per authenticated session — the
 * theme counterpart of the language adoption in the auth context (which
 * cannot own this: packages/auth must not depend on packages/ui). The
 * once-per-session guard keeps a mid-session manual change from being
 * overwritten by stale server state; it resets on logout.
 *
 * Steps aside while an application's colour mode is in force: the pages of
 * that flow keep the application's look, and the profile is not written.
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

    if (profileTheme && isTheme(profileTheme) && !readFlowHints().theme) {
      setTheme(profileTheme)
    }
  }, [status, profileTheme, setTheme])

  return null
}
