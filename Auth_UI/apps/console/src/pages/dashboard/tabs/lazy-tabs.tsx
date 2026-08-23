import * as React from "react"

import { Skeleton } from "@authsystem/ui/skeleton"

import { TAB_LOADERS } from "./tab-loaders"

/**
 * The dashboard tabs, loaded on demand.
 *
 * Not a page-weight nicety. The router resolves a matched route's module before
 * it renders anything, and the auth guard is a render-time component - so a
 * signed-out visitor who typed the bare origin fetched the dashboard, and with
 * it the charting library behind every tab, before being redirected to sign in.
 * Behind a boundary the tab is fetched when it is shown, which for that visitor
 * is never.
 */
export const OverviewTab = React.lazy(TAB_LOADERS.overview)
export const SecurityTab = React.lazy(TAB_LOADERS.security)
export const PeopleTab = React.lazy(TAB_LOADERS.people)
export const AppsTab = React.lazy(TAB_LOADERS.apps)
export const AuditTab = React.lazy(TAB_LOADERS.audit)

/** Holds the tab's height while its chunk arrives, so the page does not jump. */
export function TabFallback() {
  return (
    <div className="mt-4 grid gap-4 md:grid-cols-2">
      <Skeleton className="h-64 w-full" />
      <Skeleton className="h-64 w-full" />
    </div>
  )
}
