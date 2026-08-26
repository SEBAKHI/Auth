import { useMutation } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"

/**
 * The most rows the server will write into one file.
 *
 * Mirrors `ExportAuditLogsCommandValidator`, which rejects anything larger. It
 * is duplicated here because the console is the only side that can warn BEFORE
 * the request: it already knows how many rows the same filters matched.
 */
export const MAX_EXPORT_RECORDS = 10000

export type AuditLogExportFormat = "csv" | "json"

/** Every filter the export takes, in the shape the request body wants. */
export type AuditLogExportFilters = Record<string, unknown>

/**
 * The audit export, wherever it is offered.
 *
 * One mutation and one download, because the two surfaces that offer it differ
 * only in their chrome and in what they narrow by — and the part that must
 * never differ is the body. A second copy would be a second place to forget the
 * participant pin, and forgetting it on a person's page means a button on one
 * timeline writing the whole platform's history to disk.
 *
 * A hook rather than one component doing everything: the page hangs its trigger
 * off `PageHeader`, the tab has no page header, and the permission gate belongs
 * at each call site where it can be read rather than inside something shared
 * that decides quietly.
 */
export function useAuditLogExport({
  filters,
  totalCount,
}: {
  /** Spread into the request body verbatim. Include every pin the screen applies. */
  filters: AuditLogExportFilters
  /** Rows the same filters matched, for the truncation warning. */
  totalCount: number
}) {
  const { t } = useTranslation()

  const mutation = useMutation({
    mutationFn: async (format: AuditLogExportFormat) => {
      const { data, error, response } = await api.POST(
        "/api/v1/audit-logs/export",
        {
          body: { format, ...filters, maxRecords: MAX_EXPORT_RECORDS },
          parseAs: "blob",
        }
      )
      if (error) throw error
      return {
        blob: data as unknown as Blob,
        // The server names the file after what it holds — the participant, the
        // role, and whether the cap cut it short. The console used to overwrite
        // that with a constant, so a one-person extract reached disk under the
        // same name as a whole-table one.
        fileName: fileNameFrom(response) ?? `audit-logs.${format}`,
      }
    },
    onSuccess: ({ blob, fileName }) => {
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement("a")
      anchor.href = url
      anchor.download = fileName
      anchor.click()
      URL.revokeObjectURL(url)
      toast.success(t("auditLogs.exported"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return { mutation, willTruncate: totalCount > MAX_EXPORT_RECORDS }
}

/**
 * The filename the server chose, if the browser was allowed to see it.
 *
 * Cross-origin, `Content-Disposition` reaches script only when the API lists it
 * in `Access-Control-Expose-Headers`; both CORS providers now do. The fallback
 * stays because a missing header must not produce a download named "undefined".
 */
function fileNameFrom(response: Response): string | undefined {
  const header = response.headers.get("content-disposition")
  if (!header) return undefined
  // RFC 5987 form first — it is the one that survives a non-ASCII name.
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header)
  if (encoded) return decodeURIComponent(encoded[1].trim())
  const plain = /filename="?([^";]+)"?/i.exec(header)
  return plain ? plain[1].trim() : undefined
}
