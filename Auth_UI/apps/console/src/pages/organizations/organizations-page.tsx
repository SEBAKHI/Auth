import { OrganizationsPage } from "@astoom/account/pages/organizations/organizations-page"
import { useAuth } from "@astoom/auth/auth-context"
import { PERMISSIONS } from "@/lib/constants"
import { OrganizationsAdminPage } from "./organizations-admin-page"

/**
 * Platform admins see every organization; everyone else falls back to the
 * membership-scoped self-service list (the same page the accounts app uses).
 */
export function ConsoleOrganizationsPage() {
  const { hasPermission } = useAuth()

  return hasPermission(PERMISSIONS.organizations.read) ? (
    <OrganizationsAdminPage />
  ) : (
    <OrganizationsPage />
  )
}
