import { Download } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"

import {
  MAX_EXPORT_RECORDS,
  useAuditLogExport,
  type AuditLogExportFilters,
  type AuditLogExportFormat,
} from "./use-audit-log-export"

/**
 * The export control: a format menu, plus the confirmation a partial file needs.
 *
 * The cap was a server-side log line — the caller received the most recent ten
 * thousand rows and nothing in the response, the file, or its name said the
 * rest existed. On a whole-table export that was already wrong; on a person's
 * timeline read from both sides it is the ordinary case for any administrator,
 * where the reader's whole model of the file is "this is their complete
 * history".
 *
 * The gate stays with the caller: this renders whatever it is given.
 */
export function AuditLogExportMenu({
  filters,
  totalCount,
}: {
  filters: AuditLogExportFilters
  totalCount: number
}) {
  const { t } = useTranslation()
  const { mutation, willTruncate } = useAuditLogExport({ filters, totalCount })
  const [pending, setPending] = React.useState<AuditLogExportFormat>()

  const start = (format: AuditLogExportFormat) => {
    if (willTruncate) {
      setPending(format)
      return
    }
    mutation.mutate(format)
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" disabled={mutation.isPending}>
            <Download data-icon="inline-start" />
            {t("auditLogs.export")}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuGroup>
            <DropdownMenuItem onClick={() => start("csv")}>
              CSV
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => start("json")}>
              JSON
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DropdownMenuContent>
      </DropdownMenu>

      <ConfirmDialog
        open={Boolean(pending)}
        onOpenChange={(open) => !open && setPending(undefined)}
        title={t("auditLogs.exportTruncatedTitle")}
        description={t("auditLogs.exportTruncatedBody", {
          limit: MAX_EXPORT_RECORDS,
          total: totalCount,
        })}
        confirmLabel={t("auditLogs.exportTruncatedConfirm")}
        onConfirm={() => {
          if (pending) mutation.mutate(pending)
          setPending(undefined)
        }}
      />
    </>
  )
}
