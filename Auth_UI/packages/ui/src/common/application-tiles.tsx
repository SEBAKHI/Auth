import { useQuery } from "@tanstack/react-query"
import { AppWindow, TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { launchTarget } from "@authsystem/auth/launch-application"
import { Button } from "@authsystem/ui/button"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@authsystem/ui/empty"
import { Item, ItemContent, ItemMedia, ItemTitle } from "@authsystem/ui/item"
import { Skeleton } from "@authsystem/ui/skeleton"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"
import { cn } from "@authsystem/ui/utils"

export type MyApplicationDto = Schemas["MyApplicationDto"]

/** The caller's own list; signing out clears it with every other user-scoped entry. */
export const MY_APPLICATIONS_QUERY_KEY = ["me", "applications"] as const

/**
 * Where the tiles are drawn. "page": the My applications page (Home in the
 * accounts app), large tiles as many to a row as fit. "panel": the popover the
 * header's grid button opens, three smaller tiles to a row, as Google's.
 */
export type ApplicationTilesVariant = "page" | "panel"

const LAYOUT: Record<
  ApplicationTilesVariant,
  {
    grid: string
    skeleton: string
    skeletons: number
    avatar: "lg" | "xl"
    item: "default" | "xs"
  }
> = {
  page: {
    // One column per 9rem of width, as many as fit: a single tile stays
    // tile-sized instead of stretching across the page, and 375px still holds two.
    grid: "grid grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] gap-4",
    skeleton: "h-36",
    skeletons: 6,
    avatar: "xl",
    item: "default",
  },
  panel: {
    grid: "grid grid-cols-3 gap-1",
    skeleton: "h-24",
    skeletons: 6,
    avatar: "lg",
    item: "xs",
  },
}

/** What a list of applications is, wherever it came from. */
export interface ApplicationTilesState {
  data: MyApplicationDto[] | undefined
  isPending: boolean
  isError: boolean
  refetch: () => unknown
}

/**
 * The applications the signed-in person can open, as tiles with the name under
 * the logo, each opening its application in a new tab (UI batch item 12, and
 * the header's grid button). One definition for the page and the panel, so the
 * two can never disagree about which applications show or how one opens.
 *
 * The data comes from the caller, so a surface that reads the list some other
 * way (a page of the accounts app framed inside a linked application) draws
 * exactly these tiles. Every row is re-checked here, whatever its source: the
 * launch address must pass the rule an accepted invitation's launch uses
 * (`launchTarget`), and be https only as the server requires for a tile, so a
 * value that is not a web address never becomes a link. Such a row is dropped,
 * never drawn as a tile that cannot open.
 */
export function ApplicationTiles({
  state,
  label,
  variant = "page",
  onOpen,
}: {
  state: ApplicationTilesState
  /** The accessible name of the list. */
  label: string
  variant?: ApplicationTilesVariant
  /** Called after a tile is activated (the panel closes itself). */
  onOpen?: () => void
}) {
  const { t } = useTranslation()
  const layout = LAYOUT[variant]

  const applications = (state.data ?? []).flatMap((application) => {
    const href = launchTarget(application.launchUrl)
    return href?.startsWith("https://") ? [{ application, href }] : []
  })

  if (state.isPending) {
    // `role="list"` restated on both lists: Safari stops announcing a list
    // whose markers are removed, and the reset stylesheet removes them.
    return (
      <ul
        role="list"
        className={layout.grid}
        aria-busy="true"
        aria-label={t("common.loading")}
      >
        {Array.from({ length: layout.skeletons }, (_, index) => (
          <li key={index}>
            <Skeleton className={cn(layout.skeleton, "w-full rounded-2xl")} />
          </li>
        ))}
      </ul>
    )
  }

  if (state.isError && !state.data) {
    // Only when there is nothing to show: a failed background refetch keeps
    // the tiles already on screen.
    return (
      <Empty>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <TriangleAlert />
          </EmptyMedia>
          <EmptyTitle>{t("common.error")}</EmptyTitle>
        </EmptyHeader>
        <Button variant="outline" size="sm" onClick={() => void state.refetch()}>
          {t("common.retry")}
        </Button>
      </Empty>
    )
  }

  if (applications.length === 0) {
    return (
      <Empty>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <AppWindow />
          </EmptyMedia>
          <EmptyTitle>{t("myApplications.emptyTitle")}</EmptyTitle>
          <EmptyDescription>{t("myApplications.emptyDescription")}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    )
  }

  return (
    <ul role="list" className={layout.grid} aria-label={label}>
      {applications.map(({ application, href }) => (
        <li key={application.id}>
          <ApplicationTile
            application={application}
            href={href}
            variant={variant}
            onOpen={onOpen}
          />
        </li>
      ))}
    </ul>
  )
}

/** The signed-in person's own applications, from the self-service endpoint. */
export function useMyApplications(): ApplicationTilesState {
  return useQuery({
    queryKey: MY_APPLICATIONS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/Users/me/applications")),
  })
}

/**
 * The person's tiles as the header's panel draws them. Its own export so the
 * panel can load it on first open: the list is fetched only while the panel
 * is open, and none of this code is in the shell's first download.
 */
export function MyApplicationsPanelTiles({ onOpen }: { onOpen?: () => void }) {
  const { t } = useTranslation()
  const state = useMyApplications()
  return (
    <ApplicationTiles
      state={state}
      label={t("myApplications.launcher")}
      variant="panel"
      onOpen={onOpen}
    />
  )
}

/**
 * A link, not a button: it goes somewhere (rules/security.md, "navigates to a
 * record = <a>"), so it opens in a new tab with the browser's own gestures too.
 * `noopener` keeps the application from reaching back into this tab through
 * `window.opener`; `noreferrer` keeps this page's address out of its logs.
 */
function ApplicationTile({
  application,
  href,
  variant,
  onOpen,
}: {
  application: MyApplicationDto
  href: string
  variant: ApplicationTilesVariant
  onOpen?: () => void
}) {
  const { t } = useTranslation()
  const resolvedTheme = useResolvedTheme()
  const layout = LAYOUT[variant]

  // Stacked from the top, not centred: a name that wraps to two lines must not
  // lift its logo above the logos beside it in the same row. No border (owner,
  // 2026-11-01): the tile shows only on hover and focus, from the preset.
  return (
    <Item
      asChild
      size={layout.item}
      className="h-full flex-col flex-nowrap text-center"
    >
      <a href={href} target="_blank" rel="noopener noreferrer" onClick={onOpen}>
        {/* Hidden from the accessibility tree: the name below already names the
            link, and the image's alt (or its initials) would say it twice. */}
        <ItemMedia aria-hidden="true">
          {/* A logo in a circle keeps its whole drawing (`contain`); one that
              fails to load falls back to the name's initials. */}
          <EntityAvatar
            src={pickLogo(
              application.logoUrl ?? null,
              application.logoUrlDark ?? null,
              resolvedTheme
            )}
            name={application.name}
            size={layout.avatar}
            fit="contain"
          />
        </ItemMedia>
        <ItemContent className="w-full min-w-0 flex-none items-center">
          {/* Two lines at most; the whole name on hover. No display class
              here: one after `line-clamp-2` overrides its -webkit-box and the
              name grows to every line it needs. `dir="auto"`: the name keeps
              its own direction, so a Latin name cut short on an Arabic page
              loses its end, not its first letters. */}
          <ItemTitle
            dir="auto"
            className="line-clamp-2 w-full break-words"
            title={application.name}
          >
            {application.name}
          </ItemTitle>
        </ItemContent>
        <span className="sr-only">{t("myApplications.opensInNewTab")}</span>
      </a>
    </Item>
  )
}
