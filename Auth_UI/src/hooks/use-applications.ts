import { useQuery } from "@tanstack/react-query"

import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"

/** Loads applications for use in selects/filters (cached, large page). */
export function useApplications() {
  return useQuery({
    queryKey: ["applications", "options"],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications", {
          params: { query: { pageNumber: 1, pageSize: 100 } },
        })
      ),
    staleTime: 5 * 60_000,
  })
}
