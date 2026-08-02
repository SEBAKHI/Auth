import { useMutation } from "@tanstack/react-query"
import { toast } from "sonner"

import { uploadImage } from "@authsystem/api/upload"
import { getErrorMessage } from "@authsystem/api/errors"

/**
 * Change/Remove handlers for an entity logo (organization/application/platform),
 * for use with `AvatarMenu`. `persist` writes the uploaded logo key (or null on
 * remove) onto the entity via its update endpoint; `invalidate` refreshes the
 * view. Uploads go up exactly as chosen — cropping is the user's decision, so
 * the client never trims or reframes an image.
 */
export function useLogo(opts: {
  persist: (logoKey: string | null) => Promise<void>
  invalidate: () => void
  successMessage: string
}) {
  const changeMutation = useMutation({
    mutationFn: async (file: File) => {
      const { key } = await uploadImage(file)
      await opts.persist(key)
    },
    onSuccess: () => {
      opts.invalidate()
      toast.success(opts.successMessage)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const removeMutation = useMutation({
    mutationFn: () => opts.persist(null),
    onSuccess: () => {
      opts.invalidate()
      toast.success(opts.successMessage)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return {
    onChange: (file: File) => changeMutation.mutate(file),
    onRemove: () => removeMutation.mutate(),
    pending: changeMutation.isPending || removeMutation.isPending,
  }
}
