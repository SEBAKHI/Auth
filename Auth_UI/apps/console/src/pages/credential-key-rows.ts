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
