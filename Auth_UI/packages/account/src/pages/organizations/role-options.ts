import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"

/**
 * The organization's own role lists. Its pickers used to read the platform-wide
 * ones (`/Roles`, `/Applications/{id}/roles`), which need platform permissions an
 * organization owner does not hold: the request failed with 403 and the picker
 * showed an empty list, so the owner could neither invite nor assign anyone.
 *
 * Both lists are decided by the server — which membership roles may be handed
 * out, and which application roles this caller holds enough to assign — so the
 * pickers render them as given and never filter them again here.
 */

/** The membership roles an invitation or a role change may give. */
export function useMembershipRoles(orgId: string, enabled = true) {
  return useQuery({
    queryKey: ["org-membership-roles", orgId],
    enabled,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/membership-roles", {
          params: { path: { id: orgId } },
        })
      ),
  })
}

/** The roles of one of the organization's applications the caller may assign. */
export function useAssignableAppRoles(
  orgId: string,
  applicationId: string | undefined,
  enabled = true
) {
  return useQuery({
    queryKey: ["org-assignable-app-roles", orgId, applicationId],
    enabled: enabled && Boolean(applicationId),
    queryFn: () =>
      unwrap(
        api.GET(
          "/api/v1/Organizations/{orgId}/applications/{applicationId}/assignable-roles",
          {
            params: {
              path: { orgId, applicationId: applicationId as string },
            },
          }
        )
      ),
  })
}

/** The organization's enabled applications, shared with the applications tab. */
export function useOrganizationApplications(orgId: string, enabled = true) {
  return useQuery({
    queryKey: ["org-apps", orgId],
    enabled,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/applications", {
          params: { path: { id: orgId } },
        })
      ),
  })
}
