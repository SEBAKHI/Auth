import * as React from "react"
import { ShieldCheck, type LucideIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { NavLink, Outlet, useLocation } from "react-router-dom"

import { Separator } from "@authsystem/ui/separator"
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
} from "@authsystem/ui/sidebar"
import { useActiveTimeZone } from "@authsystem/i18n/timezone"
import { useLanguage } from "@authsystem/i18n/direction"
import { BrandingLogo, useBranding } from "@authsystem/ui/branding"
import { AppBreadcrumbs } from "@authsystem/ui/common/app-breadcrumbs"
import { LanguageToggle } from "@authsystem/ui/common/language-toggle"
import { ThemeToggle } from "@authsystem/ui/common/theme-toggle"
import { UserMenu } from "@authsystem/ui/common/user-menu"

export interface AppNavItem {
  /** i18n key under `nav.*`. */
  titleKey: string
  /** Absolute route path. */
  url: string
  icon: LucideIcon
}

interface AppShellProps {
  /** Sidebar entries, already filtered for the current user. */
  navItems: AppNavItem[]
  /** i18n key under `nav.*` for the sidebar group label. */
  navGroupKey: string
  /** i18n key under `nav.*` for the breadcrumb home crumb (the `/` route). */
  homeKey: string
  /** Forwarded to the header UserMenu. */
  profileHref?: string
  /** Forwarded to the header UserMenu. */
  showProfile?: boolean
  /**
   * App-specific header controls, placed before the language and theme
   * toggles. The console puts its settings search here; the accounts app
   * passes nothing and is unaffected.
   */
  headerExtras?: React.ReactNode
}

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

function AppSidebar({
  navItems,
  navGroupKey,
}: Pick<AppShellProps, "navItems" | "navGroupKey">) {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const { dir } = useLanguage()
  const branding = useBranding()

  return (
    <Sidebar collapsible="icon" side={dir === "rtl" ? "right" : "left"}>
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild>
              <NavLink to="/">
                <BrandingLogo
                  className="h-9 w-auto max-w-40 object-contain group-data-[collapsible=icon]:size-8"
                  fallback={
                    <div className="flex aspect-square size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
                      <ShieldCheck className="size-5" />
                    </div>
                  }
                />
                {/* A logo usually carries the brand name; avoid repeating it. */}
                {branding.logoUrl ? null : (
                  <span className="truncate font-semibold">
                    {branding.name}
                  </span>
                )}
              </NavLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>{t(`nav.${navGroupKey}`)}</SidebarGroupLabel>
          <SidebarMenu>
            {navItems.map((item) => {
              const Icon = item.icon
              const label = t(`nav.${item.titleKey}`)
              const isActive =
                item.url === "/"
                  ? pathname === "/"
                  : pathname === item.url || pathname.startsWith(`${item.url}/`)

              return (
                <SidebarMenuItem key={item.url}>
                  <SidebarMenuButton asChild isActive={isActive} tooltip={label}>
                    <NavLink to={item.url}>
                      <Icon />
                      <span>{label}</span>
                    </NavLink>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              )
            })}
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
    </Sidebar>
  )
}

/** Authenticated application shell: sidebar + header + routed content. */
export function AppShell({
  navItems,
  navGroupKey,
  homeKey,
  profileHref,
  showProfile,
  headerExtras,
}: AppShellProps) {
  return (
    // The shell is exactly one viewport tall and never scrolls itself, so the
    // header — breadcrumbs, settings search, account menu — stays put however
    // long the page below it runs. Scrolling belongs to `main`.
    <SidebarProvider className="h-svh overflow-hidden">
      <AppSidebar navItems={navItems} navGroupKey={navGroupKey} />
      <SidebarInset className="min-h-0 overflow-hidden">
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          <SidebarTrigger />
          <Separator orientation="vertical" className="h-6" />
          <AppBreadcrumbs homeKey={homeKey} />
          <div className="ms-auto flex items-center gap-1">
            {headerExtras}
            <LanguageToggle />
            <ThemeToggle />
            <UserMenu profileHref={profileHref} showProfile={showProfile} />
          </div>
        </header>
        {/* `min-h-0` so this can actually shrink inside the flex column; without
            it a tall page pushes the shell past the viewport again. Pages that
            fill the height (list pages with their own scrolling table) render a
            `h-full` root and never make this scroll. */}
        <main className="flex min-h-0 flex-1 flex-col overflow-y-auto p-4 md:p-6">
          <ZonedOutlet />
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
