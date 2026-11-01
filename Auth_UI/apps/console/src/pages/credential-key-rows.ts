import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"

type RevocableKey = {
  isRevoked?: boolean
  revokeReason?: string | null
  revokeReasonCode?: string | null
}

/**
 * The codes the system writes when it revokes a key itself, as
 * `KeyRevocationReasons.All` in `Auth.Domain/Constants/KeyRevocationReasons.cs`
 * lists them; `credential-key-rows.test.ts` reads that file. Each is named in
 * `apiKeys.revokeReasonCodes` in every language (Q6, owner 2026-10-31).
 */
export const KEY_REVOCATION_REASON_CODES = ["ApplicationDeleted"] as const

export type KeyRevocationReasonCode =
  (typeof KEY_REVOCATION_REASON_CODES)[number]

function isKnownCode(code: string): code is KeyRevocationReasonCode {
  return (KEY_REVOCATION_REASON_CODES as readonly string[]).includes(code)
}

/**
 * Why a key was revoked, as its reader should read it: a reason the system
 * wrote is named in the reader's language (its raw code when this console does
 * not know it yet), and an administrator's reason is shown as they typed it.
 */
export function revokeReasonText(
  row: RevocableKey,
  t: TFunction
): string | undefined {
  const code = row.revokeReasonCode?.trim()
  if (code) {
    return isKnownCode(code) ? t(`apiKeys.revokeReasonCodes.${code}`) : code
  }
  return row.revokeReason?.trim() ? row.revokeReason : undefined
}

/**
 * A key's row panel lists every field the API sends. The revocation reason
 * belongs there only when the key is revoked and has a reason: on a live key,
 * or on a revoked one whose administrator left the reason empty, a
 * "Revocation reason: —" row says nothing. Where it belongs it is the text
 * `revokeReasonText` gives, so the panel, the column and the export all say it
 * in the reader's language; the code it came from is not a row of its own.
 * Shared by the API-key and webhook-key tables, on the main pages and on an
 * application's tabs. Make it again when the language changes.
 */
export function revokeReasonForReader(t: TFunction) {
  return <T extends RevocableKey>(rows: readonly T[]): T[] =>
    rows.map((row) => {
      const text = row.isRevoked ? revokeReasonText(row, t) : undefined
      if (!("revokeReason" in row) && !("revokeReasonCode" in row)) return row
      const copy = { ...row }
      delete copy.revokeReasonCode
      if (text) copy.revokeReason = text
      else delete copy.revokeReason
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
