import { Building2, House, UserRound } from "lucide-react"

import { AppShell, type AppNavItem } from "@authsystem/ui/common/app-shell"

import { PolicyUpdateNotice } from "./policy-update-notice"

const NAV: AppNavItem[] = [
  { titleKey: "home", url: "/", icon: House },
  { titleKey: "profile", url: "/profile", icon: UserRound },
  { titleKey: "organizations", url: "/organizations", icon: Building2 },
]

/** Accounts shell: the shared sidebar layout with the self-service nav. */
export function AccountShell() {
  return (
    <>
      <PolicyUpdateNotice />
      <AppShell
        navItems={NAV}
        navGroupKey="account"
        // Home (`/`) is this app's landing page and the first crumb of every
        // trail, so it is named as the nav names it.
        homeKey="home"
        homeHref="/"
        // The sidebar already links to the profile; skip the avatar-menu entry.
        showProfile={false}
      />
    </>
  )
}
