import { useQueryClient } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import type { Schemas } from "@authsystem/api/types"
import { useAuth } from "@authsystem/auth/auth-context"

/**
 * Fire-and-forget sync of profile preferences (language, theme, …) to the
 * API, so the next login — and the other apps — follow the user's choice.
 * The change stays local-only when the user is anonymous or the call fails.
 */
export function usePreferenceSync() {
  const { status } = useAuth()
  const queryClient = useQueryClient()

  return (body: Schemas["UpdateProfileRequest"]) => {
    if (status !== "authenticated") {
      return
    }

    void api
      .PUT("/api/v1/Users/me", { body })
      .then(({ error }) => {
        if (!error) {
          void queryClient.invalidateQueries({ queryKey: ["me"] })
        }
      })
      .catch(() => {
        /* preference stays local-only if the sync fails */
      })
  }
}
