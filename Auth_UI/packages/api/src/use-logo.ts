import { useMutation } from "@tanstack/react-query"
import { toast } from "sonner"

import { uploadImage } from "@astoom/api/upload"
import { getErrorMessage } from "@astoom/api/errors"
import { trimLogoFile } from "@astoom/api/trim-logo"

/**
 * Change/Remove handlers for an entity logo (organization/application), for use
 * with `AvatarMenu`. `persist` writes the uploaded logo key (or null on remove)
 * onto the entity via its update endpoint; `invalidate` refreshes the view.
 */
export function useLogo(opts: {
  persist: (logoKey: string | null) => Promise<void>
  invalidate: () => void
  successMessage: string
  /**
   * Trim padded margins before upload. Only wanted where the logo renders at
   * its natural aspect ratio (platform wordmark/favicon). Logos shown inside
   * a circular avatar must keep their margins — like user photos — or the
   * circle clips the content edges.
   */
  trim?: boolean
}) {
  const changeMutation = useMutation({
    mutationFn: async (file: File) => {
      const { key } = await uploadImage(
        opts.trim ? await trimLogoFile(file) : file
      )
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
