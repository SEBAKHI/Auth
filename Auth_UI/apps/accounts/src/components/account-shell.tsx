import { Building2, UserRound } from "lucide-react"

import { AppShell, type AppNavItem } from "@authsystem/ui/common/app-shell"

import { PolicyUpdateNotice } from "./policy-update-notice"

const NAV: AppNavItem[] = [
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
        homeKey="account"
        // `/` only redirects here, so the profile IS this app's landing page.
        // Saying so is what stops the phone-width back link offering a way up
        // from it - a "‹ Account" that pointed at `/` and bounced straight back.
        homeHref="/profile"
        // The sidebar already links to the profile; skip the avatar-menu entry.
        showProfile={false}
      />
    </>
  )
}
