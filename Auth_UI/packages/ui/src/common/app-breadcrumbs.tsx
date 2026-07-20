import * as React from "react"
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
 * Header breadcrumb trail: Home › … › section › record. Every matched route
 * that carries `handle.crumb` contributes one crumb, so a section nested under
 * a parent section (notifications › templates) shows the parent as a link back.
 * The record name is published by detail pages via `usePageBreadcrumb`.
 */
export function AppBreadcrumbs({
  homeKey,
}: {
  /** i18n key under `nav.*` for the home crumb, linking to `/`. */
  homeKey: string
}) {
  const { t } = useTranslation()
  const matches = useMatches()
  const override = useBreadcrumbOverride()

  const crumbs = matches
    .map((match) => (match.handle as CrumbHandle | undefined)?.crumb)
    .filter((crumb) => crumb !== undefined)
    // A layout route and its index child can name the same destination; the
    // trail should show it once.
    .filter((crumb, index, all) => all.findIndex(c => c.href === crumb.href) === index)

  const last = crumbs.at(-1)
  const isHome = !last || last.titleKey === homeKey
  const isDetail = Boolean(last?.detail)

  return (
    <Breadcrumb className="min-w-0">
      <BreadcrumbList className="flex-nowrap">
        <BreadcrumbItem>
          {isHome ? (
            <BreadcrumbPage>{t(`nav.${homeKey}`)}</BreadcrumbPage>
          ) : (
            <BreadcrumbLink asChild>
              <Link to="/">{t(`nav.${homeKey}`)}</Link>
            </BreadcrumbLink>
          )}
        </BreadcrumbItem>

        {isHome
          ? null
          : crumbs.map((crumb, index) => {
              // Every crumb but the last links to its own list page; the last
              // one links only when a record crumb follows it.
              const isLast = index === crumbs.length - 1
              const asLink = !isLast || isDetail

              return (
                <React.Fragment key={crumb.href}>
                  <BreadcrumbSeparator />
                  <BreadcrumbItem className="min-w-0">
                    {asLink ? (
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
                </React.Fragment>
              )
            })}

        {isDetail ? (
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
