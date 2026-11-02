import type * as React from "react"

import { Button } from "@authsystem/ui/button"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { DropdownMenuLabel } from "@authsystem/ui/dropdown-menu"

/*
 * The avatar and the identity row of the header's user menu, apart from the menu
 * (`user-menu.tsx`) because that module reads the session through the API client,
 * which a linked application's account bar must never load. The bar draws these
 * same pieces.
 */

/** The menu's width, wherever it opens (the header, a linked application's account bar). */
export const USER_MENU_CONTENT_CLASS = "w-56"

/**
 * The avatar button at the end of the header: the one the menu opens from here,
 * and in a linked application's account bar.
 */
export function UserAvatarButton({
  name,
  src,
  ...props
}: Omit<React.ComponentProps<typeof Button>, "children"> & {
  name: string
  src: string | null | undefined
}) {
  return (
    <Button variant="ghost" size="icon-header" aria-label={name} {...props}>
      {/* As large as the buttons beside it (owner, 2026-11-02): smaller,
          it read as the one control drawn at another size. */}
      <EntityAvatar src={src} name={name} className="size-header-button" />
    </Button>
  )
}

/** Who the menu belongs to: its first row, wherever it opens. */
export function UserMenuIdentity({
  name,
  email,
  src,
}: {
  name: string
  email: string | null | undefined
  src: string | null | undefined
}) {
  return (
    <DropdownMenuLabel>
      <div className="flex items-center gap-2">
        <EntityAvatar src={src} name={name} className="size-8" />
        <div className="flex min-w-0 flex-col">
          <span className="truncate text-sm font-medium">{name}</span>
          <span className="truncate text-xs font-normal text-muted-foreground">
            {email}
          </span>
        </div>
      </div>
    </DropdownMenuLabel>
  )
}
