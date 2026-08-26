import type { ColumnDef } from "@tanstack/react-table"
import { Eye } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { formatDateTime } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"

import { useAuditLabels } from "./audit-log-labels"
import { ResultBadge } from "./result-badge"

type AuditLogDto = Schemas["AuditLogDto"]

/**
 * Every column an audit row has, in the order every audit table shows them.
 *
 * One list, because there is one kind of row. The two tables that read it had
 * been written out separately, and the copy on a user's page never received any
 * of the three fixes the other one got: it showed `user.locked` instead of
 * "Account locked", it had no outcome at all — so an attempted lockout and a
 * completed one looked identical — and it named neither the person who acted
 * nor the person acted upon, which are the two questions an audit trail exists
 * to answer.
 *
 * A surface chooses which of these to open with, never which exist. The
 * distinction matters: `meta.covers` below is what stops the shared table
 * offering `performedByEmail`, `userEmail` and `isSuccess` a second time as raw
 * auto-discovered columns, and a column that is not defined covers nothing.
 * See `ColumnMeta.covers` and `ColumnMeta.defaultHidden`.
 */
export const AUDIT_LOG_COLUMN_IDS = [
  "action",
  "actionType",
  "result",
  "entityType",
  "actor",
  "subject",
  "applicationName",
  "timestamp",
] as const

export type AuditLogColumnId = (typeof AUDIT_LOG_COLUMN_IDS)[number]

export interface AuditLogColumnsOptions {
  /** Ids to start hidden on this surface; they stay in the columns menu. */
  defaultHidden?: readonly AuditLogColumnId[]
  /**
   * Ids this surface narrows on the SERVER, which therefore get no in-table
   * filter chip.
   *
   * A faceted chip reads its options out of the rows already loaded, so beside
   * a control that re-queries the whole table it is the weaker of two things
   * wearing one name — and its option list, being one page of values, reads as
   * the complete set of applications when it is a sample of them.
   */
  serverFiltered?: readonly AuditLogColumnId[]
  /** Opens the detail dialog, from the trailing action column. */
  onViewDetail: (log: AuditLogDto) => void
}

export function useAuditLogColumns({
  defaultHidden,
  serverFiltered,
  onViewDetail,
}: AuditLogColumnsOptions): ColumnDef<AuditLogDto, unknown>[] {
  const { t } = useTranslation()
  const { actionLabel, actionTypeLabel } = useAuditLabels()

  return React.useMemo(() => {
    const hidden = new Set<AuditLogColumnId>(defaultHidden ?? [])
    const onServer = new Set<AuditLogColumnId>(serverFiltered ?? [])
    const isHidden = (id: AuditLogColumnId) =>
      hidden.has(id) ? { defaultHidden: true } : {}
    const facet = (id: AuditLogColumnId) =>
      onServer.has(id) ? {} : { filterVariant: "faceted" as const }

    const columns: ColumnDef<AuditLogDto, unknown>[] = [
      {
        id: "action",
        accessorFn: (row) => row.action ?? "",
        header: t("auditLogs.action"),
        meta: { label: t("auditLogs.action"), ...isHidden("action") },
        cell: ({ row }) => (
          <div className="min-w-0">
            <p className="truncate font-medium">
              {actionLabel(row.original.action ?? "")}
            </p>
            {/* The stored value, kept in view: it is what a support ticket, a
                URL filter and a SQL query all need, and it is the same string
                in every language. Isolated, because on an RTL page a bare code
                inherits the page direction and truncates from the wrong end. */}
            <p className="truncate text-xs text-muted-foreground">
              <bdi dir="auto">{row.original.action}</bdi>
            </p>
          </div>
        ),
      },
      {
        id: "actionType",
        accessorFn: (row) => row.actionType ?? "",
        header: t("auditLogs.actionType"),
        meta: { label: t("auditLogs.actionType"), ...isHidden("actionType") },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.actionType
              ? actionTypeLabel(row.original.actionType)
              : "—"}
          </span>
        ),
      },
      {
        id: "result",
        accessorFn: (row) => String(row.isSuccess ?? ""),
        // Nothing in SortFields orders by outcome, and the page-level filter is
        // the way to gather one anyway.
        enableSorting: false,
        header: t("auditLogs.result"),
        // Without the declaration the same field came back as an "Is Success"
        // column reading yes/no — and reading it wrong, since a row whose
        // outcome was never recorded has no field at all and rendered as an em
        // dash next to a badge that says so properly.
        meta: {
          label: t("auditLogs.result"),
          covers: ["isSuccess"],
          ...isHidden("result"),
        },
        cell: ({ row }) => <ResultBadge value={row.original.isSuccess} />,
      },
      {
        id: "entityType",
        accessorFn: (row) => row.entityType ?? "",
        filterFn: "faceted",
        header: t("auditLogs.target"),
        meta: {
          label: t("auditLogs.target"),
          ...facet("entityType"),
          ...isHidden("entityType"),
        },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.entityType ?? "—"}
          </span>
        ),
      },
      {
        // Who DID it. This column read the subject once, under this same
        // heading — so an account an administrator locked was listed as having
        // locked itself, and the one question an audit trail exists to answer
        // was answered with the wrong name.
        id: "actor",
        accessorFn: (row) => row.performedByEmail ?? row.performedByName ?? "",
        header: t("auditLogs.actor"),
        // All three, including the id: its auto column resolves to the same
        // name this cell falls back to, so it was a third heading for one
        // person.
        meta: {
          label: t("auditLogs.actor"),
          covers: ["performedBy", "performedByName", "performedByEmail"],
          ...isHidden("actor"),
        },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.performedByEmail ??
              row.original.performedByName ??
              "—"}
          </span>
        ),
      },
      {
        // Who it happened TO. The two are the same person only when someone
        // acts on their own account, and different in every administrative
        // event.
        id: "subject",
        accessorFn: (row) => row.userEmail ?? row.userName ?? "",
        header: t("auditLogs.subject"),
        meta: {
          label: t("auditLogs.subject"),
          covers: ["userId", "userName", "userEmail"],
          ...isHidden("subject"),
        },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.userEmail ?? row.original.userName ?? "—"}
          </span>
        ),
      },
      {
        id: "applicationName",
        accessorFn: (row) => row.applicationName ?? "",
        filterFn: "faceted",
        // `fields.application`, singular, is what the shared table already
        // calls this field wherever it discovers it on its own. The heading
        // names one application because the cell holds one; `nav.applications`
        // is the sidebar's plural and reads as a list in six of seven
        // languages.
        header: t("fields.application"),
        meta: {
          label: t("fields.application"),
          ...facet("applicationName"),
          covers: ["applicationId", "applicationName"],
          ...isHidden("applicationName"),
        },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.applicationName ?? "—"}
          </span>
        ),
      },
      {
        id: "timestamp",
        accessorFn: (row) => row.timestamp ?? "",
        header: t("auditLogs.timestamp"),
        meta: { label: t("auditLogs.timestamp"), ...isHidden("timestamp") },
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {formatDateTime(row.original.timestamp)}
          </span>
        ),
      },
    ]

    return [
      ...columns,
      {
        id: "actions",
        enableSorting: false,
        enableHiding: false,
        header: () => <span className="sr-only">{t("common.actions")}</span>,
        cell: ({ row }) => (
          <div className="text-end">
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={t("common.view")}
              onClick={() => onViewDetail(row.original)}
            >
              <Eye />
            </Button>
          </div>
        ),
      },
    ]
  }, [
    t,
    actionLabel,
    actionTypeLabel,
    defaultHidden,
    serverFiltered,
    onViewDetail,
  ])
}
