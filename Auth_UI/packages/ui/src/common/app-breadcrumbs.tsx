import { ChevronLeftIcon } from "lucide-react"
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
import { cn } from "@authsystem/ui/utils"
import { useBreadcrumbOverride, type CrumbHandle } from "@authsystem/ui/crumbs"

/** The matched trail, and where the current page sits in it. */
function useCrumbTrail(homeKey: string) {
  const matches = useMatches()
  const crumbs = matches
    .map((match) => (match.handle as CrumbHandle | undefined)?.crumb)
    .filter((crumb) => crumb !== undefined)
    // A layout route and its index child can name the same destination; the
    // trail should show it once.
    .filter(
      (crumb, index, all) =>
        all.findIndex((c) => c.href === crumb.href) === index
    )

  const last = crumbs.at(-1)
  return {
    crumbs,
    isHome: !last || last.titleKey === homeKey,
    isDetail: Boolean(last?.detail),
  }
}

/**
 * One level up, for screens too narrow to carry a trail.
 *
 * A breadcrumb answers two questions: where am I, and how do I go up. On a
 * phone the first is already answered - better - by the page title directly
 * below this, so only the second is left, and it needs one link rather than
 * three. Squeezing the full trail into the header instead gave every crumb
 * about twenty pixels, which answered neither question: "الإشـ… › الق…".
 *
 * This is the guidance Nielsen Norman publishes for small screens - show the
 * immediate parent only, never wrap a trail onto a second line - and the
 * convention both mobile platforms already use for their back affordance.
 */
export function ParentLink({
  homeKey,
  className,
}: {
  homeKey: string
  className?: string
}) {
  const { t } = useTranslation()
  const { crumbs, isHome, isDetail } = useCrumbTrail(homeKey)

  if (isHome) return null

  // On a record page the last crumb IS the parent - it names the list the
  // record belongs to. On a list page the parent is the crumb before it, or
  // home when the list is top level.
  const parent = isDetail ? crumbs.at(-1) : crumbs.at(-2)

  return (
    <Link
      to={parent?.href ?? "/"}
      className={cn(
        // A full-width row of its own, so the label is never the thing that
        // gives way. `w-fit` keeps the tap target to the text it labels.
        "mb-3 flex w-fit items-center gap-1 text-sm text-muted-foreground transition-colors hover:text-foreground",
        className
      )}
    >
      <ChevronLeftIcon className="size-4 rtl:rotate-180" aria-hidden="true" />
      <span data-slot="parent-link-label">
        {t(`nav.${parent?.titleKey ?? homeKey}`)}
      </span>
    </Link>
  )
}

/**
 * Header breadcrumb trail: Home › … › section › record. Every matched route
 * that carries `handle.crumb` contributes one crumb, so a section nested under
 * a parent section (notifications › templates) shows the parent as a link back.
 * The record name is published by detail pages via `usePageBreadcrumb`.
 */
export function AppBreadcrumbs({
  homeKey,
  className,
}: {
  /** i18n key under `nav.*` for the home crumb, linking to `/`. */
  homeKey: string
  /** The host hides the trail where the header has no room for it. */
  className?: string
}) {
  const { t } = useTranslation()
  const override = useBreadcrumbOverride()
  const { crumbs, isHome, isDetail } = useCrumbTrail(homeKey)

  return (
    // `min-w-0` lets the trail give way to the header controls beside it, and
    // `overflow-hidden` is what makes that safe: without it the list keeps its
    // own width and simply paints outside the box, over whatever sits next to
    // it. The separators cannot shrink, so per-crumb truncation alone still
    // leaves a long trail spilling on the narrowest phones.
    <Breadcrumb className={cn("min-w-0 overflow-hidden", className)}>
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
