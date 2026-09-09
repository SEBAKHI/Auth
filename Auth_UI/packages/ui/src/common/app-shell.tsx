import * as React from "react"
import { ShieldCheck, type LucideIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { NavLink, Outlet, useLocation } from "react-router-dom"

import { buttonVariants } from "@authsystem/ui/button"
import { Separator } from "@authsystem/ui/separator"
import { cn } from "@authsystem/ui/utils"
import { useIsMobile } from "@authsystem/ui/hooks/use-mobile"
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
  useSidebar,
} from "@authsystem/ui/sidebar"
import { useActiveTimeZone } from "@authsystem/i18n/timezone"
import { useLanguage } from "@authsystem/i18n/direction"
import { BrandingLogo, useBranding } from "@authsystem/ui/branding"
import { AppBreadcrumbs, ParentLink } from "@authsystem/ui/common/app-breadcrumbs"
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
  /**
   * i18n key under `nav.*` naming the sidebar's navigation landmark. Announced,
   * never drawn — see the group in `AppSidebar` for why it is not painted.
   */
  navGroupKey: string
  /** i18n key under `nav.*` for the breadcrumb home crumb. */
  homeKey: string
  /**
   * The app landing route, when it is not `/`. The accounts app redirects
   * `/` to `/profile`, so without this the breadcrumbs read its landing page
   * as an inner page and offer a way "up" that lands right back on it.
   */
  homeHref?: string
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

/**
 * The platform mark, as it appears wherever the shell links home.
 *
 * One definition for its two homes — the desktop header bar, and the nav
 * drawer a phone opens over the page — so the mark, its fallback and the
 * decision to drop the name text can never drift apart between them.
 *
 * The box it gets is fixed at `h-9` and never squeezed. A wordmark is wider
 * than it is tall, and the collapsed rail used to force it into a 32px square:
 * the aspect ratio held, so the name shrank to a few unreadable pixels of
 * height. Nothing here may reintroduce a width that depends on the sidebar.
 */
function BrandMark() {
  const branding = useBranding()

  return (
    <>
      <BrandingLogo
        className="h-9 w-auto max-w-40 object-contain"
        fallback={
          <div className="flex aspect-square size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <ShieldCheck className="size-5" />
          </div>
        }
      />
      {/* A logo usually carries the brand name; avoid repeating it.
          While branding is still unknown neither is shown: rendering
          the name meant every cold load flashed the compiled-in
          product name before the real logo replaced it. */}
      {branding.isPending || branding.logoUrl ? null : (
        <span className="max-w-40 truncate font-semibold">{branding.name}</span>
      )}
    </>
  )
}

function AppSidebar({
  navItems,
  navGroupKey,
  homeHref = "/",
}: Pick<AppShellProps, "navItems" | "navGroupKey" | "homeHref">) {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const { dir } = useLanguage()
  const { isMobile, setOpenMobile } = useSidebar()

  // On a phone the sidebar is a Sheet drawn over the page, so a link followed
  // from inside it lands on the new page with the nav still covering it.
  const closeOnMobile = React.useCallback(() => {
    if (isMobile) setOpenMobile(false)
  }, [isMobile, setOpenMobile])

  // Closing on the route change covers navigation that does not start with a
  // tap on a link here — the browser's Back button, most of all. The handler
  // below covers the opposite case: tapping the entry for the page you are
  // already on, where the route never changes.
  React.useEffect(() => {
    closeOnMobile()
  }, [pathname, closeOnMobile])

  return (
    <Sidebar collapsible="icon" side={dir === "rtl" ? "right" : "left"}>
      <SidebarHeader>
        {isMobile ? (
          // The drawer is opened from the header bar and covers it, so the
          // mark has to be repeated here — it is the only brand the visitor
          // can see while the nav is open. The collapse control has no meaning
          // on a phone: this sidebar is a drawer, and it is either open or gone.
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton size="lg" asChild>
                <NavLink to={homeHref} onClick={closeOnMobile}>
                  <BrandMark />
                </NavLink>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        ) : (
          // `h-10` inside the header's `p-2` makes this band exactly as tall as
          // the header bar next to it, so the collapse control and the mark that
          // replaced it sit on one line across the seam, and the group label
          // below starts level with the page content.
          <div className="flex h-10 items-center">
            {/* `ms-1` puts the icon on the same 20px gutter as every nav icon
                below it, and it stays there collapsed: the rail is wide enough
                for this button plus that offset on either side, so the control
                is centred in the rail without ever being moved to get there. */}
            <SidebarTrigger className="ms-1" />
          </div>
        )}
      </SidebarHeader>
      <SidebarContent>
        {/* The group heading is announced, not drawn. Painted, it collapsed
            with the rail — it carries `-mt-8` in the icon state — and pulled
            every entry below it 32px up and back down on each toggle. As a
            the name of the navigation landmark it still tells a screen reader
            which section these links belong to, without occupying a line that
            can disappear. */}
        <SidebarGroup role="navigation" aria-label={t(`nav.${navGroupKey}`)}>
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
                    <NavLink to={item.url} onClick={closeOnMobile}>
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
  homeHref,
  profileHref,
  showProfile,
  headerExtras,
}: AppShellProps) {
  // The same store `SidebarProvider` reads, so the header and the sidebar can
  // never disagree about which of them is holding the collapse control.
  const isMobile = useIsMobile()

  return (
    // The shell is exactly one viewport tall and never scrolls itself, so the
    // header — breadcrumbs, settings search, account menu — stays put however
    // long the page below it runs. Scrolling belongs to `main`.
    <SidebarProvider className="h-svh overflow-hidden">
      <AppSidebar
        navItems={navItems}
        navGroupKey={navGroupKey}
        homeHref={homeHref}
      />
      <SidebarInset className="min-h-0 overflow-hidden">
        <header className="flex h-14 shrink-0 items-center gap-2 border-b px-4">
          {/* The mark and the collapse control trade places at exactly the width
              where the sidebar stops being a rail and becomes a drawer. Below
              it the control cannot live inside the sidebar: the sidebar is off
              screen, and a control you cannot reach is no way to open anything.
              Above it the sidebar is always present, so the control belongs on
              the thing it collapses and the mark gets a slot that never shrinks. */}
          {isMobile ? (
            <SidebarTrigger />
          ) : (
            <NavLink
              to={homeHref ?? "/"}
              // `px-2` inside the header's `px-4` starts the mark on the same
              // 24px gutter the page content below it uses.
              className={cn(
                buttonVariants({ variant: "ghost", size: "lg" }),
                "shrink-0 gap-2 px-2"
              )}
            >
              <BrandMark />
            </NavLink>
          )}
          <Separator orientation="vertical" className="h-6" />
          {/* The header carries the trail only where it fits. Below `lg` the
              search box, language, theme and account controls leave it about a
              hundred pixels for three crumbs and two separators, and every
              crumb truncates to two characters. `ParentLink` takes over there. */}
          <AppBreadcrumbs
            homeKey={homeKey}
            homeHref={homeHref}
            className="hidden lg:flex"
          />
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
          {/* Above the page title, where it has the full content width and sits
              next to the heading it relates to rather than next to global
              controls it has nothing to do with. `shrink-0` so it never eats
              into a page that manages its own height. */}
          <ParentLink
            homeKey={homeKey}
            homeHref={homeHref}
            className="shrink-0 lg:hidden"
          />
          <ZonedOutlet />
        </main>
      </SidebarInset>
    </SidebarProvider>
  )
}
