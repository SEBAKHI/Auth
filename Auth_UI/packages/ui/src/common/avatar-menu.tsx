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
import type { FrameMode } from "@authsystem/ui/common/image-frame/frame"

// The frame editor is fetched when a file has been chosen, never before: no
// page pays for it until someone uploads, and the sign-in pages never do.
const ImageFrameDialog = React.lazy(
  () => import("@authsystem/ui/common/image-frame/image-frame-dialog")
)

/** Mirrors the server's ImageStorage:MaxSizeBytes so oversized files fail fast with a clear message. */
const MAX_IMAGE_BYTES = 10 * 1024 * 1024

/**
 * Clickable avatar with a View / Change / Remove menu. "Change" opens a file
 * picker and calls `onChange(file)`; "Remove" calls `onRemove`; "View" opens a
 * lightbox. Used where the image is editable (profile page, user/org/app detail).
 *
 * A chosen file opens the frame editor first (crop and margin, in `frame`
 * mode); `onChange` receives what it saves - the original file itself when
 * the frame was left as it was.
 */
export function AvatarMenu({
  src,
  name,
  size = "lg",
  fit,
  frame,
  onChange,
  onRemove,
  pending,
}: {
  src?: string | null
  name?: string | null
  size?: "default" | "sm" | "lg" | "xl"
  fit?: "cover" | "contain"
  /** How the editor frames the image: a free-aspect logo, a square favicon, a person's photo. */
  frame: FrameMode
  onChange: (file: File) => void
  onRemove: () => void
  pending?: boolean
}) {
  const { t } = useTranslation()
  const inputRef = React.useRef<HTMLInputElement>(null)
  const triggerRef = React.useRef<HTMLButtonElement>(null)
  const [menuOpen, setMenuOpen] = React.useState(false)
  const [viewOpen, setViewOpen] = React.useState(false)
  // One editor per chosen file: the sequence keys it, so choosing again
  // mounts a fresh editor seeded from the new file.
  const [framing, setFraming] = React.useState<{ file: File; seq: number } | null>(null)
  const framingSeq = React.useRef(0)

  return (
    <>
      {/* The frame editor hands focus back to this button when it closes, so
          the button stays focusable while the upload runs (aria-disabled, not
          disabled - a disabled button cannot take focus, which would drop it
          on <body>), and the menu simply refuses to open meanwhile. */}
      <DropdownMenu
        open={menuOpen}
        onOpenChange={(next) => setMenuOpen(next && !pending)}
      >
        <DropdownMenuTrigger asChild>
          <button
            ref={triggerRef}
            type="button"
            aria-label={t("common.avatar")}
            aria-disabled={pending || undefined}
            className="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring aria-disabled:opacity-50"
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
              framingSeq.current += 1
              setFraming({ file, seq: framingSeq.current })
            }
          }
          e.target.value = ""
        }}
      />

      {framing ? (
        <React.Suspense fallback={null}>
          <ImageFrameDialog
            key={framing.seq}
            file={framing.file}
            mode={frame}
            returnFocus={() => triggerRef.current?.focus()}
            onCancel={() => setFraming(null)}
            onConfirm={(file) => {
              setFraming(null)
              onChange(file)
            }}
          />
        </React.Suspense>
      ) : null}

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
