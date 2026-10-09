import type { ColumnDef } from "@tanstack/react-table"

import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"

/** A table cell is rendered per row; the theme is read here, not per column. */
function ThemedAvatar({
  src,
  darkSrc,
  name,
  size,
  fit,
}: {
  src: string | null | undefined
  darkSrc: string | null | undefined
  name: string | null | undefined
  size?: "default" | "sm" | "lg" | "xl"
  fit?: "cover" | "contain"
}) {
  const resolvedTheme = useResolvedTheme()
  return (
    <EntityAvatar
      src={pickLogo(src ?? null, darkSrc ?? null, resolvedTheme)}
      name={name}
      size={size}
      fit={fit}
    />
  )
}

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
  /**
   * The dark-mode image, for logos that have one: shown while the dark theme
   * is active, with `getSrc`'s image standing in when it is absent.
   */
  getDarkSrc?: (row: T) => string | null | undefined
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
    cell: ({ row }) =>
      opts.getDarkSrc ? (
        <ThemedAvatar
          src={opts.getSrc(row.original)}
          darkSrc={opts.getDarkSrc(row.original)}
          name={opts.getName(row.original)}
          size={opts.size}
          fit={opts.fit}
        />
      ) : (
        <EntityAvatar
          src={opts.getSrc(row.original)}
          name={opts.getName(row.original)}
          size={opts.size}
          fit={opts.fit}
        />
      ),
  }
}
