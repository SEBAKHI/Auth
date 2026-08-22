import { NAV_ITEMS, type NavItem } from "@/lib/constants"
import type { PermissionCheck } from "@/lib/notification-destinations"

/** Resolves permission-aware destinations and removes inaccessible nav rows. */
export function resolveNavItems(hasPermission: PermissionCheck): NavItem[] {
  return NAV_ITEMS.flatMap((item) => {
    if (!hasPermission(item.permission)) return []
    const url = item.resolveUrl ? item.resolveUrl(hasPermission) : item.url
    if (!url) return []
    return [{ ...item, url }]
  })
}
