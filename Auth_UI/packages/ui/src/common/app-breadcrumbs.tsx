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
} from "@authsystem/ui/breadcrumb"
import { Skeleton } from "@authsystem/ui/skeleton"
import { useBreadcrumbOverride, type CrumbHandle } from "@authsystem/ui/crumbs"

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
    // `min-w-0` lets the trail give way to the header controls beside it, and
    // `overflow-hidden` is what makes that safe: without it the list keeps its
    // own width and simply paints outside the box, over whatever sits next to
    // it. The separators cannot shrink, so per-crumb truncation alone still
    // leaves a long trail spilling on the narrowest phones.
    <Breadcrumb className="min-w-0 overflow-hidden">
      <BreadcrumbList className="flex-nowrap">
        {/* Every crumb truncates, this one included: on the home page it is the
            only crumb there is, and it carries the page title. */}
        <BreadcrumbItem className="min-w-0">
          {isHome ? (
            <BreadcrumbPage className="truncate">
              {t(`nav.${homeKey}`)}
            </BreadcrumbPage>
          ) : (
            <BreadcrumbLink asChild>
              <Link to="/" className="truncate">
                {t(`nav.${homeKey}`)}
              </Link>
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
