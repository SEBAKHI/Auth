import { describe, expect, it } from "vitest"

import { revokeReasonOnlyWhenGiven } from "./credential-key-rows"

/**
 * A key's row panel lists every field it is given, so the revocation reason
 * reaches it only on a revoked key that has one (owner, 2026-10-31).
 */
describe("revokeReasonOnlyWhenGiven", () => {
  it("keeps the reason of a revoked key that has one", () => {
    const revoked = { id: "a", isRevoked: true, revokeReason: "Leaked" }

    expect(revokeReasonOnlyWhenGiven([revoked])).toEqual([revoked])
  })

  it("drops the field from a live key and from a revoked key without a reason", () => {
    const rows = revokeReasonOnlyWhenGiven([
      { id: "live", isRevoked: false, revokeReason: null },
      { id: "no-reason", isRevoked: true, revokeReason: null },
      { id: "blank", isRevoked: true, revokeReason: "   " },
    ])

    for (const row of rows) expect(row).not.toHaveProperty("revokeReason")
    expect(rows.map((row) => row.id)).toEqual(["live", "no-reason", "blank"])
  })
})
