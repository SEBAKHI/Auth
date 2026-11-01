import { renderHook } from "@testing-library/react"
import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"
import { describe, expect, it } from "vitest"

import "@authsystem/i18n"
import { buildDisplayColumns } from "@authsystem/ui/data-table/auto-columns"

import { useApiKeyColumns } from "./api-keys/api-key-columns"
import { revokeReasonForReader } from "./credential-key-rows"
import { useWebhookKeyColumns } from "./webhook-keys/webhook-key-columns"

/**
 * The key tables' columns do not depend on their rows. The rows drop the
 * revocation reason where there is none (the row panel shows it only when
 * given), so a reason the table had to DISCOVER would appear in the columns
 * menu for one list and not for another. It is declared instead, hidden by
 * default, on both key kinds.
 */

const t = ((key: string) => key) as unknown as TFunction

const key = (extra: Record<string, unknown>) => ({
  id: crypto.randomUUID(),
  applicationId: "33333333-3333-3333-3333-333333333333",
  applicationName: "Shop",
  name: "Key",
  keyPrefix: "ak_prod_",
  targetUrl: "https://shop.example.com/hooks",
  environment: "production",
  createdAt: "2026-10-01T09:00:00Z",
  createdBy: "55555555-5555-5555-5555-555555555555",
  isRevoked: false,
  revokedAt: null,
  revokeReason: null,
  scopes: [],
  ...extra,
})

const LIVE_ONLY = revokeReasonForReader(t)([key({}), key({})])
const WITH_REASON = revokeReasonForReader(t)([
  key({}),
  key({ isRevoked: true, revokedAt: "2026-10-20T09:00:00Z", revokeReason: "Leaked" }),
])

function display(columns: ColumnDef<never, unknown>[], rows: unknown[]) {
  const { columns: shown, autoColumnIds } = buildDisplayColumns(
    columns,
    rows as never[],
    t
  )
  return {
    ids: shown.map((column) => column.id),
    autoColumnIds,
    reason: shown.find((column) => column.id === "revokeReason"),
  }
}

describe.each([
  ["API keys", () => useApiKeyColumns({})],
  ["webhook keys", () => useWebhookKeyColumns({})],
])("the %s table's columns", (_name, useColumns) => {
  it("are the same for live keys only and for a list with a revoked key's reason", () => {
    const { result } = renderHook(useColumns)
    const columns = result.current as ColumnDef<never, unknown>[]

    const live = display(columns, LIVE_ONLY)
    const mixed = display(columns, WITH_REASON)

    expect(live.ids).toEqual(mixed.ids)
    expect(live.ids).toContain("revokeReason")
    // Declared, not discovered, and hidden until the reader shows it.
    expect(live.autoColumnIds).not.toContain("revokeReason")
    expect(mixed.autoColumnIds).not.toContain("revokeReason")
    expect(live.reason?.meta?.defaultHidden).toBe(true)
  })
})
