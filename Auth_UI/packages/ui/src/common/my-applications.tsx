import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import {
  ApplicationTiles,
  type ApplicationTilesState,
} from "@authsystem/ui/common/application-tiles"

/** The caller's own list; signing out clears it with every other user-scoped entry. */
export const MY_APPLICATIONS_QUERY_KEY = ["me", "applications"] as const

/**
 * The signed-in person's own applications, from the self-service endpoint. Apart
 * from the tiles (`application-tiles.tsx`) because it needs the API client, which
 * the account bar's frames must never load.
 */
export function useMyApplications(): ApplicationTilesState {
  return useQuery({
    queryKey: MY_APPLICATIONS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/Users/me/applications")),
  })
}

/**
 * The person's tiles as the header's panel draws them. Its own export so the
 * panel can load it on first open: the list is fetched only while the panel
 * is open, and none of this code is in the shell's first download.
 */
export function MyApplicationsPanelTiles({ onOpen }: { onOpen?: () => void }) {
  const { t } = useTranslation()
  const state = useMyApplications()
  return (
    <ApplicationTiles
      state={state}
      label={t("myApplications.launcher")}
      variant="panel"
      onOpen={onOpen}
    />
  )
}
