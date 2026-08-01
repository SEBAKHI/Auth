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
    <nav className="flex flex-col gap-4 lg:w-56 lg:shrink-0">
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
    <div className="flex flex-col gap-6">
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
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
          <SectionNav groups={groups} activeKey={active?.key ?? ""} />
          {/* Generous, not narrow: the extra width of a large monitor is spent
              on a second column of fields (see SectionForm), not on stretching
              one control across the glass. */}
          <div className="min-w-0 flex-1 2xl:max-w-[80rem]">
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
