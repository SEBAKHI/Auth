import * as React from "react"
import { useTranslation } from "react-i18next"

import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { LogoAvatar } from "@authsystem/ui/common/logo-avatar"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { cn } from "@authsystem/ui/utils"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"

/**
 * One logo circle for a record that has a light-mode and a dark-mode logo.
 *
 * The page shows the logo of the mode it is being viewed in (the other one
 * standing in when that one is missing). An editor's menu offers View and
 * Change; Change opens a dialog with one circle per mode, each with its own
 * View / Change / Remove — so the page keeps a single mark, and both slots are
 * one click away.
 */
export function ThemedLogo({
  name,
  lightSrc,
  darkSrc,
  canEdit,
  persistLight,
  persistDark,
  invalidate,
  successMessage,
  dialogTitle,
  dialogDescription,
  size = "xl",
}: {
  name?: string | null
  lightSrc?: string | null
  darkSrc?: string | null
  canEdit: boolean
  persistLight: (logoKey: string | null) => Promise<void>
  persistDark: (logoKey: string | null) => Promise<void>
  invalidate: () => void
  successMessage: string
  dialogTitle: string
  dialogDescription: string
  size?: "default" | "sm" | "lg" | "xl"
}) {
  const { t } = useTranslation()
  const resolvedTheme = useResolvedTheme()
  const [viewOpen, setViewOpen] = React.useState(false)
  const [editOpen, setEditOpen] = React.useState(false)
  const src = pickLogo(lightSrc ?? null, darkSrc ?? null, resolvedTheme)

  if (!canEdit) {
    return <EntityAvatar src={src} name={name} size={size} fit="contain" />
  }

  // Each slot is painted in its own mode: a light wordmark meant for dark
  // pages is invisible on a light tile, and the other way round.
  const slots = [
    { mode: "light", src: lightSrc, persist: persistLight, label: t("platformSettings.logoLight") },
    { mode: "dark", src: darkSrc, persist: persistDark, label: t("platformSettings.logoDark") },
  ] as const

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button
            type="button"
            aria-label={t("common.avatar")}
            className="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <EntityAvatar src={src} name={name} size={size} fit="contain" />
          </button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start">
          <DropdownMenuGroup>
            <DropdownMenuItem disabled={!src} onClick={() => setViewOpen(true)}>
              {t("common.view")}
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => setEditOpen(true)}>
              {t("common.change")}
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>{dialogTitle}</DialogTitle>
            <DialogDescription>{dialogDescription}</DialogDescription>
          </DialogHeader>
          <div className="grid grid-cols-2 gap-3">
            {slots.map((slot) => (
              <div
                key={slot.mode}
                data-mode={slot.mode}
                className={cn(
                  slot.mode,
                  "flex flex-col items-center gap-2 rounded-2xl bg-background p-4 text-foreground ring-1 ring-foreground/10"
                )}
              >
                <LogoAvatar
                  src={slot.src}
                  name={name}
                  canEdit
                  persist={slot.persist}
                  invalidate={invalidate}
                  successMessage={successMessage}
                />
                <p className="text-sm text-muted-foreground">{slot.label}</p>
              </div>
            ))}
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={viewOpen} onOpenChange={setViewOpen}>
        <DialogContent size="md" className="p-2">
          {/* The lightbox is purely visual, but every Dialog still needs an
              accessible name or screen readers announce an unnamed dialog. */}
          <DialogTitle className="sr-only">{name ?? t("common.avatar")}</DialogTitle>
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
