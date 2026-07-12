import { ShieldCheck } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { NavLink, Outlet } from "react-router-dom"

import { LanguageToggle } from "@astoom/ui/common/language-toggle"
import { ThemeToggle } from "@astoom/ui/common/theme-toggle"
import { UserMenu } from "@astoom/ui/common/user-menu"
import { BrandingLogo, useBranding } from "@astoom/ui/branding"
import { Button } from "@astoom/ui/button"
import { useActiveTimeZone } from "@astoom/i18n/timezone"

/**
 * Routed content keyed on the active display time zone, so every visible
 * date/time re-renders when the user changes their profile time zone.
 */
function ZonedOutlet() {
  const timeZone = useActiveTimeZone()
  return (
    <React.Fragment key={timeZone}>
      <Outlet />
    </React.Fragment>
  )
}

const NAV: Array<{ titleKey: string; url: string }> = [
  { titleKey: "profile", url: "/profile" },
  { titleKey: "organizations", url: "/organizations" },
]

/** Authenticated accounts shell: slim header + routed content, mobile-first. */
export function AccountShell() {
  const { t } = useTranslation()
  const branding = useBranding()

  return (
    <div className="flex min-h-svh flex-col">
      <header className="flex h-14 shrink-0 items-center gap-4 border-b px-4">
        <div className="flex items-center gap-2">
          <BrandingLogo
            className="h-8 w-auto max-w-40 object-contain"
            fallback={
              <div className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
                <ShieldCheck className="size-5" />
              </div>
            }
          />
          {branding.logoUrl ? null : (
            <span className="text-sm font-semibold">{branding.name}</span>
          )}
        </div>
        <nav className="flex items-center gap-1">
          {NAV.map((item) => (
            <Button key={item.url} variant="ghost" size="sm" asChild>
              <NavLink
                to={item.url}
                className="aria-[current=page]:bg-accent aria-[current=page]:text-accent-foreground"
              >
                {t(`nav.${item.titleKey}`)}
              </NavLink>
            </Button>
          ))}
        </nav>
        <div className="ms-auto flex items-center gap-1">
          <LanguageToggle />
          <ThemeToggle />
          <UserMenu />
        </div>
      </header>
      <main className="mx-auto w-full max-w-4xl flex-1 p-4 md:p-6">
        <ZonedOutlet />
      </main>
    </div>
  )
}
