import { useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useLocation, useNavigate, useParams } from "react-router-dom"

import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import type { ThemeConfig } from "@authsystem/ui/theme/theme-config"

import { loginDesignsListHref } from "@/lib/record-hrefs"

import { designKey, designsKey, getDesign, type DesignScope } from "./login-design-api"
import type { LoginDesignReturnState } from "./login-design-columns"
import { LoginDesignEditor } from "./login-design-editor"

/**
 * A sign-in page design on its own page, the way an email template has one: linkable,
 * reachable from search and history, and leaving it with unsaved changes asks first (the
 * editor's unsaved-changes prompt guards the route).
 *
 * Back returns to the list the design was opened from, at the search, sort and filters it
 * was left with (the list hands its address over in router state); opened any other way,
 * Back goes to the owner's "Templates" tab.
 */
export function LoginDesignPage({
  scope,
  canManage,
  appearance,
  description,
}: {
  scope: DesignScope
  canManage: boolean
  appearance: ThemeConfig | null
  description: string
}) {
  const { designId = "" } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const listHref = loginDesignsListHref(scope)

  // The editor reads the same query; this one only names the page.
  const design = useQuery({
    queryKey: designKey(scope, designId),
    queryFn: () => getDesign(scope, designId),
    enabled: designId !== "",
  })
  usePageBreadcrumb(design.data?.name)

  const returnTo = returnAddress(location.state, listHref)

  const refresh = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: designsKey(scope), exact: true })
    // The console's own sign-in page shows the platform's design.
    if (scope.kind === "platform") void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
  }, [queryClient, scope])

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={design.data?.name ?? ""} description={description} />
      <LoginDesignEditor
        key={designId}
        scope={scope}
        designId={designId}
        canManage={canManage}
        appearance={appearance}
        onClose={() => {
          refresh()
          navigate(returnTo)
        }}
        onSaved={refresh}
      />
    </div>
  )
}

/**
 * The list address handed over by the list, accepted only when it is that same list (its
 * path); anything else falls back to the list's own address.
 */
function returnAddress(state: unknown, listHref: string): string {
  const returnTo = (state as Partial<LoginDesignReturnState> | null)?.returnTo
  const listPath = listHref.split("?")[0]
  return typeof returnTo === "string" && (returnTo === listPath || returnTo.startsWith(`${listPath}?`))
    ? returnTo
    : listHref
}
