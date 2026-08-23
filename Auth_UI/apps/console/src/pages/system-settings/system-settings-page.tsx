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
                    <span
                      aria-hidden
                      className="ms-auto size-2 rounded-full bg-destructive"
                    />
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
    <div className="flex flex-col gap-6 lg:min-h-0 lg:flex-1">
      <PageHeader
        title={t("systemSettings.title")}
        description={t("systemSettings.subtitle")}
      />
      {query.data?.dbOverridesUnavailable ? <DbUnavailableBanner /> : null}
      {query.data?.restartPending ? <RestartBanner /> : null}
      {query.isPending ? (
        <Skeleton className="h-64 w-full" />
      ) : query.isError ? (
        <p className="py-8 text-center text-sm text-muted-foreground">
          {t("errors.generic")}
        </p>
      ) : (
        // No `items-start`: the columns have to stretch to the row's height for
        // either of them to scroll inside it.
        <div className="flex flex-col gap-6 lg:min-h-0 lg:flex-1 lg:flex-row">
          <SectionNav groups={groups} activeKey={active?.key ?? ""} />
          {/* The card keeps the full page width: extra width is spent by the
              rows (label at the start, control pinned to the end), never by
              stretching a control.

              `lg:p-2` is not decoration. A Card is outlined by `ring-1` and
              lifted by `shadow-md`, and both paint outside its box. Setting
              `overflow-y` to anything but `visible` also forces `overflow-x` to
              `auto`, so without this padding the pane clips the card's outline
              flush against its own top and inline-start edges and the card
              reads as an unbounded slab. */}
          <div className="min-w-0 flex-1 lg:min-h-0 lg:overflow-y-auto lg:p-2">
            {active ? (
              <SectionForm
                key={`${active.key}:${active.rowVersion ?? "none"}`}
                section={active}
              />
            ) : null}
          </div>
        </div>
      )}
    </div>
  )
}
