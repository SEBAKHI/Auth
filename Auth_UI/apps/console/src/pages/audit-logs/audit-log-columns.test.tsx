import { renderHook, render } from "@testing-library/react"
import type { ColumnDef } from "@tanstack/react-table"
import type { TFunction } from "i18next"
import { beforeAll, describe, expect, it, vi } from "vitest"

import { buildDisplayColumns } from "@authsystem/ui/data-table/auto-columns"
import type { Schemas } from "@authsystem/api/types"
import i18n from "@authsystem/i18n"
import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"

import {
  AUDIT_LOG_COLUMN_IDS,
  useAuditLogColumns,
  type AuditLogColumnId,
} from "./audit-log-columns"

type AuditLogDto = Schemas["AuditLogDto"]

/**
 * One row type, one set of columns.
 *
 * The two audit tables were written out separately and only one of them ever
 * received a fix. The copy on a user's page showed `user.locked` where the
 * other showed "Account locked", had no outcome column at all, and named
 * neither the person who acted nor the person acted upon. Nothing failed: each
 * file was internally consistent, and no test compared them.
 *
 * These assertions are about the factory both tables now read, so a column that
 * gains a translation, a `covers` entry or a sort rule gains it on every screen
 * at once — and a column that loses one loses it here first.
 */

/**
 * A row carrying every field the read model returns, so `covers` can be checked
 * against the real payload rather than against a convenient subset. Mirrors the
 * fullest fixture in `e2e/isolated/column-coverage.spec.ts`.
 */
const ROW: AuditLogDto = {
  id: "11111111-1111-1111-1111-111111111111",
  userId: "22222222-2222-2222-2222-222222222222",
  userName: "Employee Name",
  userEmail: "employee@example.test",
  performedBy: "33333333-3333-3333-3333-333333333333",
  performedByName: "Admin Name",
  performedByEmail: "admin@example.test",
  applicationId: "44444444-4444-4444-4444-444444444444",
  applicationName: "Console",
  action: "user.locked",
  actionType: "Security",
  entityType: "User",
  entityId: "22222222-2222-2222-2222-222222222222",
  ipAddress: "203.0.113.10",
  userAgent: "Mozilla/5.0",
  isSuccess: true,
  timestamp: "2026-08-01T09:00:00Z",
}

const t = ((key: string) => key) as unknown as TFunction

function columnsFor(
  options: {
    defaultHidden?: readonly AuditLogColumnId[]
    serverFiltered?: readonly AuditLogColumnId[]
  } = {}
) {
  const { result } = renderHook(() =>
    useAuditLogColumns({ ...options, onViewDetail: vi.fn() })
  )
  return result.current
}

function idsOf(columns: ColumnDef<AuditLogDto, unknown>[]): string[] {
  return columns.map((column) => column.id ?? "")
}

function columnById(
  columns: ColumnDef<AuditLogDto, unknown>[],
  id: string
): ColumnDef<AuditLogDto, unknown> {
  const found = columns.find((column) => column.id === id)
  if (!found) throw new Error(`no column with id "${id}"`)
  return found
}

/** Renders one cell against a row, the way the table would. */
function renderCell(
  column: ColumnDef<AuditLogDto, unknown>,
  row: Partial<AuditLogDto>
) {
  const cell = column.cell as (context: unknown) => React.ReactElement
  return render(cell({ row: { original: row }, getValue: () => undefined }))
}

beforeAll(async () => {
  await i18n.changeLanguage("en")
})

describe("the audit columns every audit table reads", () => {
  it("defines the same columns whatever a surface chooses to show", () => {
    // The hidden set changes what opens, never what exists — the tab that hides
    // `subject` still needs its `covers` declaration below.
    expect(idsOf(columnsFor())).toEqual([...AUDIT_LOG_COLUMN_IDS, "actions"])
    expect(
      idsOf(
        columnsFor({
          defaultHidden: ["subject"],
          serverFiltered: ["applicationName"],
        })
      )
    ).toEqual([...AUDIT_LOG_COLUMN_IDS, "actions"])
  })

  it("marks exactly the chosen columns hidden by default", () => {
    const columns = columnsFor({ defaultHidden: ["subject"] })
    expect(columnById(columns, "subject").meta?.defaultHidden).toBe(true)
    expect(columnById(columns, "actor").meta?.defaultHidden).toBeUndefined()
  })

  /**
   * A chip reading its options out of the loaded page, beside a select that
   * re-queries the whole table, is the weaker of two controls wearing one name
   * — and it offers one page of applications as though they were all of them.
   */
  it("offers no in-table filter for a field the surface narrows on the server", () => {
    const shared = columnsFor()
    expect(columnById(shared, "applicationName").meta?.filterVariant).toBe(
      "faceted"
    )

    const narrowed = columnsFor({ serverFiltered: ["applicationName"] })
    expect(
      columnById(narrowed, "applicationName").meta?.filterVariant
    ).toBeUndefined()
    // Only the named one: the target facet has no server-side counterpart.
    expect(columnById(narrowed, "entityType").meta?.filterVariant).toBe(
      "faceted"
    )
  })

  /**
   * The rule `sortable-columns.ts` states in prose: "A column the API cannot
   * order by must also carry `enableSorting: false`, or its header offers a
   * click that breaks the list." `server-sort-contract.test.ts` holds the list
   * to the C# allow-list; nothing held the columns to the list.
   */
  it("offers a sort only on the fields the endpoint orders by", () => {
    const allowed = SORTABLE_COLUMNS.auditLogs as readonly string[]
    for (const column of columnsFor()) {
      const id = column.id ?? ""
      expect(
        allowed.includes(id) || column.enableSorting === false,
        `"${id}" is not in the endpoint's allow-list and does not disable sorting`
      ).toBe(true)
    }
  })

  /**
   * The other direction, and the one that fails silently: both surfaces
   * sanitize their URL sort against this list, so an id in it that no column
   * renders is a sort key a saved link can carry and nothing on screen can
   * clear.
   */
  it("renders a column for every field the list says is sortable", () => {
    const rendered = new Set(idsOf(columnsFor()))
    for (const id of SORTABLE_COLUMNS.auditLogs) {
      expect(rendered, `nothing renders the sortable field "${id}"`).toContain(
        id
      )
    }
  })

  /**
   * The other half of the same guarantee, and the one that actually shipped
   * broken: a column named for a concept — `actor` reading `performedByEmail` —
   * declares nothing about what it consumed, so auto-discovery offered every
   * one of those fields again as a raw column. On a user's page, where no
   * `covers` was declared at all, that meant a second "Succeeded" column
   * reading Yes/No/— beside no outcome column whatsoever.
   */
  it("claims every field its cells put on screen, so none is offered twice", () => {
    const { autoColumnIds } = buildDisplayColumns(columnsFor(), [ROW], t)
    // What is left is what no curated cell shows: the row's own id, the id of
    // the thing it happened to, and the two request details.
    expect(autoColumnIds).toEqual(["id", "entityId", "ipAddress", "userAgent"])
  })

  it("names the two people apart, and falls back to a name then to nothing", () => {
    const columns = columnsFor()
    const actor = columnById(columns, "actor")
    const subject = columnById(columns, "subject")

    expect(renderCell(actor, ROW).container).toHaveTextContent(
      "admin@example.test"
    )
    expect(renderCell(subject, ROW).container).toHaveTextContent(
      "employee@example.test"
    )
    expect(
      renderCell(actor, { performedByName: "Admin Name" }).container
    ).toHaveTextContent("Admin Name")
    expect(renderCell(subject, {}).container).toHaveTextContent("—")
  })

  it("reads an outcome as three states, never as a blank", () => {
    const result = columnById(columnsFor(), "result")
    expect(renderCell(result, { isSuccess: true }).container).toHaveTextContent(
      "Succeeded"
    )
    expect(
      renderCell(result, { isSuccess: false }).container
    ).toHaveTextContent("Failed")
    // Not "—": a row whose outcome was never recorded must not read as a
    // success, and rendering nothing is the same claim with a quieter face.
    expect(renderCell(result, {}).container).toHaveTextContent("Not recorded")
  })

  it("shows the action's name and keeps the stored code beside it, isolated", () => {
    const { container } = renderCell(columnById(columnsFor(), "action"), ROW)
    expect(container).toHaveTextContent("Account locked")
    // The element AND its direction, not the string: the isolate is the RTL
    // correctness, an assertion on the text alone passes without it, and
    // `dir="auto"` is what the house rule requires — never a forced "ltr".
    const isolate = container.querySelector("bdi")
    expect(isolate?.textContent).toBe("user.locked")
    expect(isolate?.getAttribute("dir")).toBe("auto")
  })

  it("names the application in the singular, as one row holds one", () => {
    const column = columnById(columnsFor(), "applicationName")
    // `nav.applications` is the sidebar's plural and read as a list of
    // applications above a column holding exactly one.
    expect(column.meta?.label).toBe("Application")
  })
})
