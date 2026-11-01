import { useQuery } from "@tanstack/react-query"
import { AppWindow, TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { launchTarget } from "@authsystem/auth/launch-application"
import { Button } from "@authsystem/ui/button"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { PageHeader } from "@authsystem/ui/common/page-header"
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

type MyApplicationDto = Schemas["MyApplicationDto"]

/** The caller's own list; signing out clears it with every other user-scoped entry. */
const MY_APPLICATIONS_QUERY_KEY = ["me", "applications"] as const

/**
 * One column per 9rem of width, as many as fit: a single tile stays tile-sized
 * instead of stretching across the page, and 375px still holds two.
 */
const GRID = "grid grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] gap-4"

/** How many placeholder tiles the loading grid draws. */
const SKELETON_TILES = 6

/**
 * Home (UI batch item 12): the applications the signed-in person can open, as
 * large tiles with the name under the logo. Mounted at `/` in the accounts app
 * and as "My applications" in the console.
 *
 * The server lists only applications the sign-in gate admits this person to and
 * that offer a way in (owner decision D9), each with its launch address. The
 * page re-checks every address with the same rule as an accepted invitation's
 * launch (`launchTarget`), and https only as the server does for a tile, so a
 * value that is not a web address never becomes a link: such a row is dropped,
 * never drawn as a tile that cannot open.
 */
export function MyApplicationsPage() {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: MY_APPLICATIONS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/Users/me/applications")),
  })

  const applications = (query.data ?? []).flatMap((application) => {
    const href = launchTarget(application.launchUrl)
    return href?.startsWith("https://") ? [{ application, href }] : []
  })

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("myApplications.title")}
        description={t("myApplications.description")}
      />

      {query.isPending ? (
        // `role="list"` restated on both lists: Safari stops announcing a list
        // whose markers are removed, and the reset stylesheet removes them.
        <ul
          role="list"
          className={GRID}
          aria-busy="true"
          aria-label={t("common.loading")}
        >
          {Array.from({ length: SKELETON_TILES }, (_, index) => (
            <li key={index}>
              <Skeleton className="h-36 w-full rounded-2xl" />
            </li>
          ))}
        </ul>
      ) : query.isError && !query.data ? (
        // Only when there is nothing to show: a failed background refetch
        // keeps the tiles already on screen.
        <Empty>
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <TriangleAlert />
            </EmptyMedia>
            <EmptyTitle>{t("common.error")}</EmptyTitle>
          </EmptyHeader>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void query.refetch()}
          >
            {t("common.retry")}
          </Button>
        </Empty>
      ) : applications.length === 0 ? (
        <Empty>
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <AppWindow />
            </EmptyMedia>
            <EmptyTitle>{t("myApplications.emptyTitle")}</EmptyTitle>
            <EmptyDescription>
              {t("myApplications.emptyDescription")}
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <ul role="list" className={GRID} aria-label={t("myApplications.title")}>
          {applications.map(({ application, href }) => (
            <li key={application.id}>
              <ApplicationTile application={application} href={href} />
            </li>
          ))}
        </ul>
      )}
    </div>
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
}: {
  application: MyApplicationDto
  href: string
}) {
  const { t } = useTranslation()
  const resolvedTheme = useResolvedTheme()

  // Stacked from the top, not centred: a name that wraps to two lines must not
  // lift its logo above the logos beside it in the same row. No border (owner,
  // 2026-11-01): the tile shows only on hover and focus, from the preset.
  return (
    <Item asChild className="h-full flex-col flex-nowrap text-center">
      <a href={href} target="_blank" rel="noopener noreferrer">
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
            size="xl"
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
