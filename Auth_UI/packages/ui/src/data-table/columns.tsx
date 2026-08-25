import type { ColumnDef } from "@tanstack/react-table"

import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"

/**
 * Shared leading avatar column for user/organization/application tables.
 * Place the result first in a page's `columns` array. Having no accessor, it
 * is automatically excluded from CSV export and auto-column discovery.
 *
 * `covers` names the image field the caller reads, because the picture IS that
 * field on screen: without it every table using this column also offered a
 * `Profile Image Url` / `Logo Url` column holding the same URL as text.
 */
export function avatarColumn<T>(opts: {
  getSrc: (row: T) => string | null | undefined
  getName: (row: T) => string | null | undefined
  size?: "default" | "sm" | "lg" | "xl"
  /** Use "contain" for logo columns so marks keep their aspect ratio. */
  fit?: "cover" | "contain"
  /** Record fields the avatar renders — typically the image URL field. */
  covers?: readonly string[]
}): ColumnDef<T, unknown> {
  return {
    id: "avatar",
    enableSorting: false,
    enableHiding: false,
    meta: { covers: opts.covers },
    header: () => null,
    cell: ({ row }) => (
      <EntityAvatar
        src={opts.getSrc(row.original)}
        name={opts.getName(row.original)}
        size={opts.size}
        fit={opts.fit}
      />
    ),
  }
}
