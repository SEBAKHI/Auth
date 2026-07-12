import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"

export type UserStatusAction = "lock" | "unlock" | "activate" | "deactivate"

/**
 * Shared user account mutations (lock/unlock/activate/deactivate and delete),
 * used by both the users list page and the user detail page.
 */
export function useUserActions(options?: {
  onStatusChanged?: () => void
  onDeleted?: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const invalidateUsers = () =>
    queryClient.invalidateQueries({ queryKey: ["users"] })

  const statusAction = useMutation({
    mutationFn: async (input: {
      id: string
      action: UserStatusAction
      reason?: string
    }): Promise<string> => {
      const path = { id: input.id }
      switch (input.action) {
        case "lock": {
          const { error } = await api.POST("/api/v1/Users/{id}/lock", {
            params: { path },
            body: { reason: input.reason ?? "" },
          })
          if (error) throw error
          return "users.locked"
        }
        case "unlock": {
          const { error } = await api.POST("/api/v1/Users/{id}/unlock", {
            params: { path },
          })
          if (error) throw error
          return "users.unlocked"
        }
        case "activate": {
          const { error } = await api.POST("/api/v1/Users/{id}/activate", {
            params: { path },
          })
          if (error) throw error
          return "users.activated"
        }
        case "deactivate": {
          const { error } = await api.POST("/api/v1/Users/{id}/deactivate", {
            params: { path },
          })
          if (error) throw error
          return "users.deactivated"
        }
      }
    },
    onSuccess: (successKey) => {
      void invalidateUsers()
      toast.success(t(successKey))
      options?.onStatusChanged?.()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/api/v1/Users/{id}", {
        params: { path: { id } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidateUsers()
      toast.success(t("users.deleted"))
      options?.onDeleted?.()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return { statusAction, deleteMutation }
}
