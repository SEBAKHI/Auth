import * as React from "react"
import { Outlet } from "react-router-dom"

import { Separator } from "@astoom/ui/separator"
import {
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
} from "@astoom/ui/sidebar"
import { useActiveTimeZone } from "@astoom/i18n/timezone"
import { ACCOUNTS_URL } from "@astoom/api/env"
import { LanguageToggle } from "@astoom/ui/common/language-toggle"
import { ThemeToggle } from "@astoom/ui/common/theme-toggle"
import { UserMenu } from "@astoom/ui/common/user-menu"
import { AppBreadcrumbs } from "./app-breadcrumbs"
import { AppSidebar } from "./app-sidebar"

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

/** Authenticated application shell: sidebar + header + routed content. */
export function AppShell() {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger />
          <Separator orientation="vertical" className="h-6" />
          <AppBreadcrumbs />
          <div className="ms-auto flex items-center gap-1">
            <LanguageToggle />
            <ThemeToggle />
            {/* Self-service profile lives in the accounts app now. */}
            <UserMenu profileHref={`${ACCOUNTS_URL}/profile`} />
          </div>
        </header>
        <main className="flex-1 p-4 md:p-6">
          <ZonedOutlet />
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
