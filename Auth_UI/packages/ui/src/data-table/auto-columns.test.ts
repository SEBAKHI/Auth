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

/**
 * A column named for a concept reads fields that share none of its name. Before
 * `meta.covers` there was no way to say so, and every such column was shadowed
 * by an auto column repeating its own source under an untranslated heading —
 * `Is Active` beside `Status`, `Performed By Email` beside `Actor`.
 */
describe("declared coverage", () => {
  interface Audit {
    action: string
    performedByEmail: string
    performedByName: string
    isSuccess: boolean
    ipAddress: string
  }

  const rows: Audit[] = [
    {
      action: "user.locked",
      performedByEmail: "admin@example.test",
      performedByName: "Admin",
      isSuccess: true,
      ipAddress: "203.0.113.42",
    },
  ]

  const curated: ColumnDef<Audit, unknown>[] = [
    { accessorKey: "action", header: "Action" },
    {
      id: "actor",
      accessorFn: (row) => row.performedByEmail,
      header: "Actor",
      meta: {
        label: "Actor",
        covers: ["performedByEmail", "performedByName"],
      },
    },
    {
      id: "result",
      accessorFn: (row) => String(row.isSuccess),
      header: "Result",
      meta: { label: "Result", covers: ["isSuccess"] },
    },
  ]

  it("suppresses the auto column for every declared field", () => {
    const { autoColumnIds } = buildDisplayColumns(curated, rows, t)
    expect(autoColumnIds).toEqual(["ipAddress"])
  })

  it("still discovers the fields nobody claimed", () => {
    // The point is not to hide the record — only to stop showing one field
    // twice. Anything undeclared must keep its column.
    const { autoColumnIds } = buildDisplayColumns(curated, rows, t)
    expect(autoColumnIds).toContain("ipAddress")
  })

  it("leaves no two columns wearing the same label", () => {
    const { columns: merged } = buildDisplayColumns(curated, rows, t)
    const labels = merged
      .map((column) => column.meta?.label)
      .filter((label): label is string => Boolean(label))
    expect(new Set(labels).size).toBe(labels.length)
  })

  it("tolerates a declared field the API never returned", () => {
    // Declaring more than a payload happens to carry is how a nullable field is
    // covered at all: `DefaultIgnoreCondition.WhenWritingNull` drops it from
    // the JSON entirely, so it is absent on one page of results and present on
    // the next.
    const withGhost: ColumnDef<Audit, unknown>[] = [
      {
        id: "actor",
        accessorFn: (row) => row.performedByEmail,
        header: "Actor",
        meta: { covers: ["performedByEmail", "performedByName", "performedBy"] },
      },
    ]
    const { autoColumnIds } = buildDisplayColumns(withGhost, rows, t)
    expect(autoColumnIds).toEqual(["action", "isSuccess", "ipAddress"])
  })
})
