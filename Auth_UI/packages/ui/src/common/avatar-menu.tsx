import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { Dialog, DialogContent, DialogTitle } from "@authsystem/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"

/** Mirrors the server's ImageStorage:MaxSizeBytes so oversized files fail fast with a clear message. */
const MAX_IMAGE_BYTES = 10 * 1024 * 1024

/**
 * Clickable avatar with a View / Change / Remove menu. "Change" opens a file
 * picker and calls `onChange(file)`; "Remove" calls `onRemove`; "View" opens a
 * lightbox. Used where the image is editable (profile page, user/org/app detail).
 */
export function AvatarMenu({
  src,
  name,
  size = "lg",
  fit,
  onChange,
  onRemove,
  pending,
}: {
  src?: string | null
  name?: string | null
  size?: "default" | "sm" | "lg" | "xl"
  fit?: "cover" | "contain"
  onChange: (file: File) => void
  onRemove: () => void
  pending?: boolean
}) {
  const { t } = useTranslation()
  const inputRef = React.useRef<HTMLInputElement>(null)
  const [viewOpen, setViewOpen] = React.useState(false)

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button
            type="button"
            aria-label={t("common.avatar")}
            disabled={pending}
            className="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
          >
            <EntityAvatar src={src} name={name} size={size} fit={fit} />
          </button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start">
          <DropdownMenuGroup>
            <DropdownMenuItem disabled={!src} onClick={() => setViewOpen(true)}>
              {t("common.view")}
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => inputRef.current?.click()}>
              {t("common.change")}
            </DropdownMenuItem>
            <DropdownMenuItem
              variant="destructive"
              disabled={!src}
              onClick={onRemove}
            >
              {t("common.remove")}
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DropdownMenuContent>
      </DropdownMenu>

      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/jpeg,image/webp,image/gif"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0]
          if (file) {
            if (file.size > MAX_IMAGE_BYTES) {
              toast.error(
                t("common.imageTooLarge", {
                  mb: Math.round(MAX_IMAGE_BYTES / (1024 * 1024)),
                })
              )
            } else {
              onChange(file)
            }
          }
          e.target.value = ""
        }}
      />

      <Dialog open={viewOpen} onOpenChange={setViewOpen}>
        <DialogContent size="md" className="p-2">
          {/* The lightbox is purely visual, but every Dialog still needs an
              accessible name or screen readers announce an unnamed dialog. */}
          <DialogTitle className="sr-only">
            {name ?? t("common.avatar")}
          </DialogTitle>
          {src ? (
            <img
              src={src}
              alt={name ?? ""}
              className="mx-auto max-h-[70svh] w-auto rounded-lg object-contain"
            />
          ) : null}
        </DialogContent>
      </Dialog>
    </>
  )
}
