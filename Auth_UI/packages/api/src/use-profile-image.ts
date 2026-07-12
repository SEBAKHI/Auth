import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { uploadImage } from "@astoom/api/upload"
import { getErrorMessage } from "@astoom/api/errors"

/**
 * Change/Remove handlers for a user's profile image, for use with `AvatarMenu`.
 * Pass a `userId` for the admin path (`/Users/{id}/profile-image`); omit it for
 * the current user (`/Users/me/profile-image`).
 */
export function useProfileImage(userId?: string) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["me"] })
    void queryClient.invalidateQueries({ queryKey: ["users"] })
  }

  const changeMutation = useMutation({
    mutationFn: async (file: File) => {
      const { key } = await uploadImage(file)
      const { error } = userId
        ? await api.PUT("/api/v1/Users/{id}/profile-image", {
            params: { path: { id: userId } },
            body: { imageKey: key },
          })
        : await api.PUT("/api/v1/Users/me/profile-image", {
            body: { imageKey: key },
          })
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("profile.imageUpdated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const removeMutation = useMutation({
    mutationFn: async () => {
      const { error } = userId
        ? await api.DELETE("/api/v1/Users/{id}/profile-image", {
            params: { path: { id: userId } },
          })
        : await api.DELETE("/api/v1/Users/me/profile-image")
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("profile.imageRemoved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return {
    onChange: (file: File) => changeMutation.mutate(file),
    onRemove: () => removeMutation.mutate(),
    pending: changeMutation.isPending || removeMutation.isPending,
  }
}
