import { Building2, UserRound } from "lucide-react"

import { AppShell, type AppNavItem } from "@astoom/ui/common/app-shell"

const NAV: AppNavItem[] = [
  { titleKey: "profile", url: "/profile", icon: UserRound },
  { titleKey: "organizations", url: "/organizations", icon: Building2 },
]

/** Accounts shell: the shared sidebar layout with the self-service nav. */
export function AccountShell() {
  return (
    <AppShell
      navItems={NAV}
      navGroupKey="account"
      homeKey="account"
      // The sidebar already links to the profile; skip the avatar-menu entry.
      showProfile={false}
    />
  )
}
