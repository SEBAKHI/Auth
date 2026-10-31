import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"

type RevocableKey = { isRevoked?: boolean; revokeReason?: string | null }

/**
 * A key's row panel lists every field the API sends. The revocation reason
 * belongs there only when the key is revoked and a reason was given: on a live
 * key, or on a revoked one whose administrator left the reason empty, a
 * "Revocation reason: —" row says nothing. Shared by the API-key and
 * webhook-key tables, on the main pages and on an application's tabs.
 */
export function revokeReasonOnlyWhenGiven<T extends RevocableKey>(
  rows: readonly T[]
): T[] {
  return rows.map((row) => {
    if (row.isRevoked && row.revokeReason?.trim()) return row
    if (!("revokeReason" in row)) return row
    const copy = { ...row }
    delete copy.revokeReason
    return copy
  })
}

/**
 * The reason as a column, declared rather than discovered. The rows above drop
 * the field where there is no reason, and a field the table finds only on some
 * rows would make the columns menu offer it or not depending on the data. It
 * starts hidden on every surface; the reader can show it from the menu.
 */
export function revokeReasonColumn<T extends RevocableKey>(
  t: TFunction
): ColumnDef<T, unknown> {
  return {
    id: "revokeReason",
    accessorFn: (row) => row.revokeReason ?? "",
    // The server's sort list for these endpoints has no reason.
    enableSorting: false,
    header: t("fields.revokeReason"),
    meta: { label: t("fields.revokeReason"), defaultHidden: true },
    cell: ({ row }) => row.original.revokeReason?.trim() || "—",
  }
}
