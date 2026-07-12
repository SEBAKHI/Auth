import type { ColumnDef } from "@tanstack/react-table"

import { EntityAvatar } from "@astoom/ui/common/entity-avatar"

/**
 * Shared leading avatar column for user/organization/application tables.
 * Place the result first in a page's `columns` array. Having no accessor, it
 * is automatically excluded from CSV export and auto-column discovery.
 */
export function avatarColumn<T>(opts: {
  getSrc: (row: T) => string | null | undefined
  getName: (row: T) => string | null | undefined
  size?: "default" | "sm" | "lg" | "xl"
  /** Use "contain" for logo columns so marks keep their aspect ratio. */
  fit?: "cover" | "contain"
}): ColumnDef<T, unknown> {
  return {
    id: "avatar",
    enableSorting: false,
    enableHiding: false,
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
