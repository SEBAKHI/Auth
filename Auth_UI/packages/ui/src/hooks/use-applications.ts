import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { getErrorStatus } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"

/**
 * Loads applications for use in selects/filters (cached, large page).
 *
 * The endpoint requires `applications:read`, which several of the nine screens
 * mounting `ApplicationSelect` do NOT gate on: the API-key, webhook-key, role
 * and permission screens gate on their own resource. On those four the picker
 * feeds a required field and the create button is disabled until it is set, so
 * a holder of `apikeys:create` without `applications:read` could not create an
 * API key at all — and the only thing on screen was a disabled control reading
 * "No applications", which describes the wrong problem.
 *
 * `retry: false` so the forbidden case surfaces at once rather than after the
 * default retry, and callers get `isForbidden` to say what actually happened.
 */
export function useApplications() {
  const query = useQuery({
    queryKey: ["applications", "options"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications", {
          params: { query: { pageNumber: 1, pageSize: 100 } },
        })
      ),
    staleTime: 5 * 60_000,
    retry: false,
  })

  return {
    ...query,
    /** The list is unavailable because this account may not read applications. */
    isForbidden: getErrorStatus(query.error) === 403,
  }
}
