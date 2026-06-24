import { ShieldCheck } from "lucide-react"
import { useTranslation } from "react-i18next"
import { NavLink, useLocation } from "react-router-dom"

import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"
import { useAuth } from "@/lib/auth/auth-context"
import { NAV_ITEMS } from "@/lib/constants"
import { useLanguage } from "@/lib/i18n/direction"

export function AppSidebar() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const { pathname } = useLocation()
  const { dir } = useLanguage()

  const items = NAV_ITEMS.filter((item) => hasPermission(item.permission))

  return (
    <Sidebar collapsible="icon" side={dir === "rtl" ? "right" : "left"}>
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild>
              <NavLink to="/">
                <div className="flex aspect-square size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
                  <ShieldCheck className="size-5" />
                </div>
                <span className="truncate font-semibold">
                  {t("common.appName")}
                </span>
              </NavLink>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>{t("nav.platform")}</SidebarGroupLabel>
          <SidebarMenu>
            {items.map((item) => {
              const Icon = item.icon
              const label = t(`nav.${item.titleKey}`)
              const isActive =
                item.url === "/"
                  ? pathname === "/"
                  : pathname === item.url || pathname.startsWith(`${item.url}/`)

              return (
                <SidebarMenuItem key={item.url}>
                  <SidebarMenuButton
                    asChild
                    isActive={isActive}
                    tooltip={label}
                  >
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
