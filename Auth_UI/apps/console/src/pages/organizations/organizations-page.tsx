import { OrganizationDetailPage } from "@authsystem/account/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@authsystem/account/pages/organizations/organizations-page"
import { useAuth } from "@authsystem/auth/auth-context"
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

/**
 * Platform detail host. The shared account page stays membership-scoped unless
 * this console explicitly grants the server-backed platform capability.
 */
export function ConsoleOrganizationDetailPage() {
  const { hasPermission } = useAuth()

  return (
    <OrganizationDetailPage
      canManagePlatform={hasPermission(PERMISSIONS.organizations.manage)}
      userHref={(userId) => `/users/${userId}`}
      applicationHref={(applicationId) => `/applications/${applicationId}`}
    />
  )
}
