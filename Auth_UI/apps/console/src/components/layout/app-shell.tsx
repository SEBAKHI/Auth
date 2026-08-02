import { useAuth } from "@authsystem/auth/auth-context"
import { AppShell as SharedAppShell } from "@authsystem/ui/common/app-shell"
import { NAV_ITEMS } from "@/lib/constants"

/** Console shell: the shared sidebar layout with the permission-filtered admin nav. */
export function AppShell() {
  const { hasPermission } = useAuth()
  const items = NAV_ITEMS.filter((item) => hasPermission(item.permission))

  return (
    <SharedAppShell
      navItems={items}
      navGroupKey="platform"
      homeKey="dashboard"
      profileHref="/profile"
    />
  )
}
