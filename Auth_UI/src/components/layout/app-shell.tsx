import * as React from "react"
import { Outlet } from "react-router-dom"

import { Separator } from "@/components/ui/separator"
import {
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
} from "@/components/ui/sidebar"
import { useActiveTimeZone } from "@/lib/timezone"
import { AppSidebar } from "./app-sidebar"
import { LanguageToggle } from "./language-toggle"
import { ThemeToggle } from "./theme-toggle"
import { UserMenu } from "./user-menu"

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
          <div className="ms-auto flex items-center gap-1">
            <LanguageToggle />
            <ThemeToggle />
            <UserMenu />
          </div>
        </header>
        <main className="flex-1 p-4 md:p-6">
          <ZonedOutlet />
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
