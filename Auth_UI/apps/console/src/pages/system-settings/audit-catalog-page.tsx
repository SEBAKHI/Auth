import type { ColumnDef } from "@tanstack/react-table"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { PageHeader } from "@authsystem/ui/common/page-header"
import { DataTable } from "@authsystem/ui/data-table/data-table"

import {
  AUDIT_ACTIONS,
  auditActionI18nKey,
  auditActionTypeI18nKey,
  type AuditActionEntry,
} from "@/lib/audit-catalog"

/**
 * What this system records, in the reader's language.
 *
 * A settings surface without a setting, and that is the honest shape for it: the
 * list comes from the code that writes the audit trail, so nothing here can be
 * turned off from here. Presenting it as configurable would promise a control
 * that does not exist, which is the failure mode the audit trail itself is meant
 * to be free of.
 *
 * No request is made. Forty-nine rows already live in this bundle because the
 * audit table needs them to translate a code it is showing, and the names come
 * from this app's locale files either way — so an endpoint would buy a round
 * trip and translate nothing extra.
 */
export function AuditCatalogPage() {
  const { t } = useTranslation()

  const columns: ColumnDef<AuditActionEntry, unknown>[] = React.useMemo(
    () => [
      {
        id: "name",
        accessorFn: (row) =>
          t(`auditLogs.actions.${auditActionI18nKey(row.code)}`, {
            defaultValue: row.code,
          }),
        header: t("auditLogs.action"),
        meta: { label: t("auditLogs.action") },
        cell: ({ getValue }) => (
          <span className="font-medium">{String(getValue() ?? "")}</span>
        ),
      },
      {
        id: "code",
        accessorFn: (row) => row.code,
        header: t("auditLogs.actionCode"),
        meta: { label: t("auditLogs.actionCode") },
        cell: ({ row }) => (
          // No direction override: a dotted code is a run of directional islands
          // and reads in order in either paragraph direction. See the note in
          // searchable-select.tsx for why forcing LTR here is wrong.
          <span className="text-sm text-muted-foreground">
            <bdi dir="auto">{row.original.code}</bdi>
          </span>
        ),
      },
      {
        id: "actionType",
        accessorFn: (row) =>
          t(`auditLogs.actionTypes.${auditActionTypeI18nKey(row.actionType)}`, {
            defaultValue: row.actionType,
          }),
        filterFn: "faceted",
        header: t("auditLogs.actionType"),
        meta: { label: t("auditLogs.actionType"), filterVariant: "faceted" },
        cell: ({ getValue }) => (
          <span className="text-sm text-muted-foreground">
            {String(getValue() ?? "")}
          </span>
        ),
      },
    ],
    [t]
  )

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
      <PageHeader
        title={t("auditCatalog.title")}
        description={t("auditCatalog.subtitle")}
      />

      <DataTable
        fillHeight
        tableId="audit-catalog"
        columns={columns}
        data={AUDIT_ACTIONS as AuditActionEntry[]}
        globalSearch
        searchPlaceholder={t("auditLogs.searchAction")}
        exportFileName="audit-action-catalog"
        // Three columns and no hidden fields, so a detail panel would repeat the
        // row it was opened from.
        enableRowDetail={false}
      />
    </div>
  )
}
