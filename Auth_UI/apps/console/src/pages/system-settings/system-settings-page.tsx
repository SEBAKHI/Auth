import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { Link, Navigate, useParams } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { Button } from "@authsystem/ui/button"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { Skeleton } from "@authsystem/ui/skeleton"
import { cn } from "@authsystem/ui/utils"

import { DbUnavailableBanner, RestartBanner } from "./components/restart-banner"
import { SectionForm } from "./components/section-form"
import {
  SECTION_I18N,
  SETTINGS_QUERY_KEY,
  groupSections,
  type SystemSettingsSection,
} from "./lib/sections"

function SectionNav({
  groups,
  activeKey,
}: {
  groups: { group: string; sections: SystemSettingsSection[] }[]
  activeKey: string
}) {
  const { t } = useTranslation()
  return (
    // `pe-2` keeps the labels clear of this column's own scrollbar; the column
    // scrolls on its own from `lg` up, so reaching the last section never
    // pushes the settings card off screen.
    <nav className="flex flex-col gap-4 lg:min-h-0 lg:w-56 lg:shrink-0 lg:overflow-y-auto lg:pe-2">
      {groups.map(({ group, sections }) => (
        <div key={group} className="flex flex-col gap-1">
          <p className="px-3 text-xs font-medium text-muted-foreground">
            {t(`systemSettings.groups.${group}`, { defaultValue: group })}
          </p>
          {sections.map((section) => {
            const key = section.key ?? ""
            const pending = section.fields?.some((f) => f.isPendingRestart)
            return (
              <Button
                key={key}
                variant="ghost"
                size="sm"
                asChild
                className={cn(
                  "justify-start",
                  key === activeKey && "bg-accent text-accent-foreground"
                )}
              >
                <Link to={`/admin/system-settings/${key}`}>
                  <span className="truncate">
                    {SECTION_I18N[key]
                      ? t(`systemSettings.${SECTION_I18N[key]}.title`, {
                          defaultValue: key,
                        })
                      : key}
                  </span>
                  {pending ? (
                    <>
                      {/* The dot is the whole message for a sighted reader and
                          nothing at all for anyone else. `sr-only` is absolutely
                          positioned, so the text costs the row no width and the
                          dot keeps its `ms-auto`. */}
                      <span className="sr-only">
                        {t("systemSettings.pendingRestart")}
                      </span>
                      <span
                        aria-hidden
                        className="ms-auto size-2 rounded-full bg-destructive"
                      />
                    </>
                  ) : null}
                </Link>
              </Button>
            )
          })}
        </div>
      ))}
    </nav>
  )
}

export function SystemSettingsPage() {
  const { t } = useTranslation()
  const { sectionKey } = useParams<{ sectionKey: string }>()

  const query = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/admin/system-settings")),
  })

  const sections = query.data?.sections ?? []
  const groups = groupSections(sections)
  const active = sections.find((s) => s.key === sectionKey)

  if (query.isSuccess && sections.length > 0 && !active) {
    return (
      <Navigate
        to={`/admin/system-settings/${groups[0]?.sections[0]?.key ?? ""}`}
        replace
      />
    )
  }

  return (
    // From `lg` the page fills the shell's height instead of growing past it,
    // so the header and the banners stay put and each of the two columns below
    // carries its own scrollbar. Below that breakpoint the columns stack and
    // `main` is the single scroller again, as on every other page.
    //
    // The cap is on THIS element, not on the card: header, banners, section nav
    // and card are one column, so the title still sits over the thing it names
    // and the nav stays against the card it drives. It is a max-width on a
    // stretched flex item (`main` is `flex … flex-col`, app-shell.tsx:198), so
    // it clamps and then aligns at the cross-start — the inline start in both
    // directions, with no `mx-auto` anywhere. Centring would open a void
    // between the nav and the card and break the adjacency that makes the nav
    // read as this card's index.
    <div className="flex max-w-(--content-measure) flex-col gap-6 lg:min-h-0 lg:flex-1">
      <PageHeader
        title={t("systemSettings.title")}
        description={t("systemSettings.subtitle")}
      />
      {query.data?.dbOverridesUnavailable ? <DbUnavailableBanner /> : null}
      {query.data?.restartPending ? <RestartBanner /> : null}
      {query.isPending ? (
        <Skeleton className="h-64 w-full" />
      ) : query.isError && !query.data ? (
        // Only when there is nothing to show. A query that already has data and
        // then fails a REFETCH keeps that data, and swapping the page for this
        // paragraph would unmount the form with everything unsaved in it — the
        // exact loss this page was just fixed for, one failed background
        // request later. The 409 path refetches deliberately, which is what
        // makes a failure there reachable rather than theoretical.
        <p className="py-8 text-center text-sm text-muted-foreground">
          {t("errors.generic")}
        </p>
      ) : (
        // No `items-start`: the columns have to stretch to the row's height for
        // either of them to scroll inside it.
        <div className="flex flex-col gap-6 lg:min-h-0 lg:flex-1 lg:flex-row">
          <SectionNav groups={groups} activeKey={active?.key ?? ""} />
          {/* The card fills the page column, and the column is capped at
              `--content-measure`: extra width past that measure is spent by
              nobody. The rows still put the label at the start and pin the
              control to the end — that geometry is what aligns the controls
              into one column — but the distance they span is now bounded.

              `lg:p-2` is not decoration. A Card is outlined by `ring-1` and
              lifted by `shadow-md`, and both paint outside its box. Setting
              `overflow-y` to anything but `visible` also forces `overflow-x` to
              `auto`, so without this padding the pane clips the card's outline
              flush against its own top and inline-start edges and the card
              reads as an unbounded slab. */}
          <div className="min-w-0 flex-1 lg:min-h-0 lg:overflow-y-auto lg:p-2">
            {/* Keyed on the SECTION only. It used to carry the row version too,
                which made every refetch that changed it a full remount — and
                the 409 path refetches. So a save conflict destroyed the values
                the operator had just typed and left a toast where their work
                had been. Fresh server values now reach the form through a reset
                the form itself decides on (see `section-form`), which is the
                only place that knows whether there is anything to lose. */}
            {active ? (
              <SectionForm key={active.key ?? ""} section={active} />
            ) : null}
          </div>
        </div>
      )}
    </div>
  )
}
