import { useTranslation } from "react-i18next"
import { Link, useMatches } from "react-router-dom"

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@astoom/ui/breadcrumb"
import { Skeleton } from "@astoom/ui/skeleton"
import { useBreadcrumbOverride, type CrumbHandle } from "@astoom/ui/crumbs"

/**
 * Header breadcrumb trail: Home › section › record. Sections come from route
 * `handle.crumb` metadata; the record name is published by detail pages via
 * `usePageBreadcrumb`.
 */
export function AppBreadcrumbs() {
  const { t } = useTranslation()
  const matches = useMatches()
  const override = useBreadcrumbOverride()

  const crumb = matches
    .map((match) => (match.handle as CrumbHandle | undefined)?.crumb)
    .filter(Boolean)
    .at(-1)

  const isHome = !crumb || crumb.titleKey === "dashboard"
  const isDetail = Boolean(crumb?.detail)

  return (
    <Breadcrumb className="min-w-0">
      <BreadcrumbList className="flex-nowrap">
        <BreadcrumbItem>
          {isHome ? (
            <BreadcrumbPage>{t("nav.dashboard")}</BreadcrumbPage>
          ) : (
            <BreadcrumbLink asChild>
              <Link to="/">{t("nav.dashboard")}</Link>
            </BreadcrumbLink>
          )}
        </BreadcrumbItem>

        {crumb && !isHome ? (
          <>
            <BreadcrumbSeparator />
            <BreadcrumbItem className="min-w-0">
              {isDetail ? (
                <BreadcrumbLink asChild>
                  <Link to={crumb.href} className="truncate">
                    {t(`nav.${crumb.titleKey}`)}
                  </Link>
                </BreadcrumbLink>
              ) : (
                <BreadcrumbPage className="truncate">
                  {t(`nav.${crumb.titleKey}`)}
                </BreadcrumbPage>
              )}
            </BreadcrumbItem>
          </>
        ) : null}

        {crumb && isDetail ? (
          <>
            <BreadcrumbSeparator />
            <BreadcrumbItem className="min-w-0">
              <BreadcrumbPage className="truncate">
                {override ?? <Skeleton className="h-4 w-24" />}
              </BreadcrumbPage>
            </BreadcrumbItem>
          </>
        ) : null}
      </BreadcrumbList>
    </Breadcrumb>
  )
}
