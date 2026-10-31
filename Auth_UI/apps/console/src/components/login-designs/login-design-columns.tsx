import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, useLocation } from "react-router-dom"

import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { RecordLink } from "@authsystem/ui/common/record-link"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { UnavailableMenuItem } from "@authsystem/ui/common/unavailable-menu-item"
import { formatDateTime } from "@authsystem/ui/format"

import { loginDesignHref } from "@/lib/record-hrefs"

import type { DesignScope, LoginDesignSummary } from "./login-design-api"

/** What a row's menu asks the list to do; the list owns the dialogs and the requests. */
export type LoginDesignAction = {
  kind: "copy" | "activate" | "deactivate" | "delete"
  design: LoginDesignSummary
}

/** The columns a designs list may sort by: every column but the row menu. */
export const LOGIN_DESIGN_SORTABLE_COLUMNS = [
  "name",
  "status",
  "showPlatformLogo",
  "modifiedAt",
  "createdAt",
] as const

/** Router state a design's page reads to return to the list as the reader left it. */
export interface LoginDesignReturnState {
  returnTo: string
}

/**
 * The one definition of a sign-in page designs table, shared by the platform's
 * "Templates" tab and every application's. Each surface keeps its own
 * `tableId` and URL state; only what the columns are and show lives here.
 *
 * A design's name links to its own page (as an email template's does), and
 * carries the list's address so that page's Back lands on the same search,
 * sort and filters.
 */
export function useLoginDesignColumns({
  scope,
  canManage,
  onAction,
}: {
  scope: DesignScope
  canManage: boolean
  onAction: (action: LoginDesignAction) => void
}): ColumnDef<LoginDesignSummary, unknown>[] {
  const { t, i18n } = useTranslation()
  const location = useLocation()
  const returnState: LoginDesignReturnState = {
    returnTo: `${location.pathname}${location.search}`,
  }

  return React.useMemo<ColumnDef<LoginDesignSummary, unknown>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => row.name ?? "",
        header: t("common.name"),
        meta: { label: t("common.name") },
        cell: ({ row }) => (
          <RecordLink
            href={loginDesignHref(scope, row.original.id)}
            state={returnState}
            className="min-w-0 text-start"
          >
            {/* A design's name is the administrator's text in any language. */}
            <p className="truncate font-medium">
              <bdi>{row.original.name}</bdi>
            </p>
          </RecordLink>
        ),
      },
      {
        id: "status",
        accessorFn: (row) => (row.isActive ? "active" : "inactive"),
        filterFn: "faceted",
        header: t("common.status"),
        meta: {
          label: t("common.status"),
          covers: ["isActive"],
          filterVariant: "faceted",
          filterOptions: [
            { value: "active", label: t("common.active") },
            { value: "inactive", label: t("common.inactive") },
          ],
        },
        cell: ({ row }) =>
          row.original.isActive ? (
            <Badge>{t("common.active")}</Badge>
          ) : (
            <Badge variant="outline">{t("common.inactive")}</Badge>
          ),
      },
      {
        id: "showPlatformLogo",
        accessorFn: (row) => (row.showPlatformLogo ? "shown" : "hidden"),
        filterFn: "faceted",
        header: t("loginDesigns.platformLogo"),
        meta: {
          label: t("loginDesigns.platformLogo"),
          covers: ["showPlatformLogo"],
          filterVariant: "faceted",
          filterOptions: [
            { value: "shown", label: t("loginDesigns.platformLogoShown") },
            { value: "hidden", label: t("loginDesigns.platformLogoHidden") },
          ],
        },
        cell: ({ row }) =>
          row.original.showPlatformLogo ? (
            <span className="text-sm text-muted-foreground">
              {t("loginDesigns.platformLogoShown")}
            </span>
          ) : (
            <Badge variant="outline">{t("loginDesigns.platformLogoHidden")}</Badge>
          ),
      },
      {
        id: "modifiedAt",
        accessorFn: (row) => row.modifiedAt ?? "",
        header: t("common.modifiedAt"),
        meta: { label: t("common.modifiedAt") },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {formatDateTime(row.original.modifiedAt)}
          </span>
        ),
      },
      {
        id: "createdAt",
        accessorFn: (row) => row.createdAt ?? "",
        header: t("common.createdAt"),
        meta: { label: t("common.createdAt") },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {formatDateTime(row.original.createdAt)}
          </span>
        ),
      },
      {
        id: "actions",
        header: "",
        enableSorting: false,
        enableHiding: false,
        cell: ({ row }) => {
          const design = row.original
          return (
            <div className="flex justify-end">
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={t("loginDesigns.actions", { name: design.name })}
                  >
                    <MoreHorizontal />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuGroup>
                    <DropdownMenuItem asChild>
                      <Link to={loginDesignHref(scope, design.id) ?? ""} state={returnState}>
                        {canManage ? t("loginDesigns.edit") : t("loginDesigns.view")}
                      </Link>
                    </DropdownMenuItem>
                    {canManage ? (
                      <>
                        <DropdownMenuItem
                          onSelect={() =>
                            onAction({ kind: design.isActive ? "deactivate" : "activate", design })
                          }
                        >
                          {design.isActive ? t("loginDesigns.deactivate") : t("loginDesigns.activate")}
                        </DropdownMenuItem>
                        <DropdownMenuItem onSelect={() => onAction({ kind: "copy", design })}>
                          {t("loginDesigns.copy")}
                        </DropdownMenuItem>
                      </>
                    ) : null}
                  </DropdownMenuGroup>
                  {canManage ? (
                    <>
                      <DropdownMenuSeparator />
                      <DropdownMenuGroup>
                        {design.isActive ? (
                          <UnavailableMenuItem
                            variant="destructive"
                            reason={t("loginDesigns.deleteActiveHint")}
                          >
                            {t("loginDesigns.delete")}
                          </UnavailableMenuItem>
                        ) : (
                          <DropdownMenuItem
                            variant="destructive"
                            onSelect={() => onAction({ kind: "delete", design })}
                          >
                            {t("loginDesigns.delete")}
                          </DropdownMenuItem>
                        )}
                      </DropdownMenuGroup>
                    </>
                  ) : null}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          )
        },
      },
    ],
    // `returnState` is rebuilt from the location; its address is what matters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, i18n.language, scope, canManage, onAction, returnState.returnTo]
  )
}
