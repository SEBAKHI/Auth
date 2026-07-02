import { describe, expect, it } from "vitest"
import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"

import { buildDisplayColumns } from "./auto-columns"

interface Rec {
  email: string
  status: string
  firstName: string
  isActive: boolean
}

const t = ((key: string) => key) as unknown as TFunction

const columns: ColumnDef<Rec, unknown>[] = [
  { accessorKey: "email", header: "Email" },
  { id: "status", accessorFn: (row) => row.status, header: "Status" },
  { id: "actions", header: "" },
]

const data: Rec[] = [
  { email: "a@b.com", status: "active", firstName: "Ann", isActive: true },
]

function ids(cols: ColumnDef<Rec, unknown>[]): (string | undefined)[] {
  return cols.map((c) => c.id ?? (c as { accessorKey?: string }).accessorKey)
}

describe("buildDisplayColumns", () => {
  it("adds hidden columns for uncovered fields, before the actions column", () => {
    const { columns: merged, autoColumnIds } = buildDisplayColumns(columns, data, t)
    expect(autoColumnIds).toEqual(["firstName", "isActive"])
    expect(ids(merged)).toEqual([
      "email",
      "status",
      "firstName",
      "isActive",
      "actions",
    ])
  })

  it("does not duplicate fields already covered by an explicit column", () => {
    const { autoColumnIds } = buildDisplayColumns(columns, data, t)
    expect(autoColumnIds).not.toContain("email")
    expect(autoColumnIds).not.toContain("status")
  })

  it("returns the original columns unchanged when no rows are loaded", () => {
    const { columns: merged, autoColumnIds } = buildDisplayColumns(columns, [], t)
    expect(autoColumnIds).toEqual([])
    expect(merged).toBe(columns)
  })
})
