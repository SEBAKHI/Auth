import { describe, expect, it, vi } from "vitest"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ColumnDef } from "@tanstack/react-table"

import "@authsystem/i18n"
import { DataTable } from "./data-table"

interface Person {
  name: string
  age: number
}

const data: Person[] = [
  { name: "Charlie", age: 30 },
  { name: "Alice", age: 25 },
  { name: "Bob", age: 40 },
]

const columns: ColumnDef<Person, unknown>[] = [
  { id: "name", accessorKey: "name", header: "Name", meta: { label: "Name" } },
  { id: "age", accessorKey: "age", header: "Age", meta: { label: "Age" } },
]

/** First-column text of each body row, in display order. */
function nameColumnOrder(): string[] {
  const rows = screen.getAllByRole("row").slice(1) // drop the header row
  return rows.map((row) => within(row).getAllByRole("cell")[0].textContent ?? "")
}

describe("DataTable", () => {
  it("renders every row", () => {
    render(<DataTable columns={columns} data={data} />)
    expect(nameColumnOrder()).toEqual(["Charlie", "Alice", "Bob"])
  })

  it("sorts rows when a sortable header is clicked", async () => {
    const user = userEvent.setup()
    render(<DataTable columns={columns} data={data} />)

    const nameHeader = screen.getByRole("button", { name: "Name" })
    await user.click(nameHeader)
    expect(nameColumnOrder()).toEqual(["Alice", "Bob", "Charlie"])

    await user.click(nameHeader)
    expect(nameColumnOrder()).toEqual(["Charlie", "Bob", "Alice"])
  })

  it("exposes a column-visibility (Columns) button for hideable columns", () => {
    render(<DataTable columns={columns} data={data} />)
    expect(
      screen.getByRole("button", { name: /columns/i })
    ).toBeInTheDocument()
  })

  it("opens the detail panel when a row is clicked", async () => {
    const user = userEvent.setup()
    render(<DataTable columns={columns} data={data} />)

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
    await user.click(screen.getAllByRole("row")[1])

    const dialog = await screen.findByRole("dialog")
    expect(
      within(dialog).getByRole("heading", { name: "Details" })
    ).toBeInTheDocument()
  })

  it("does not open the detail panel when the actions cell is clicked", async () => {
    const user = userEvent.setup()
    const onAction = vi.fn()
    const withActions: ColumnDef<Person, unknown>[] = [
      ...columns,
      {
        id: "actions",
        enableSorting: false,
        enableHiding: false,
        header: () => null,
        cell: () => (
          <button type="button" onClick={onAction}>
            act
          </button>
        ),
      },
    ]

    render(<DataTable columns={withActions} data={data} />)
    await user.click(screen.getAllByRole("button", { name: "act" })[0])

    expect(onAction).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
  })

  it("lifts sorting to the page without reordering rows when server sorting is on", async () => {
    const user = userEvent.setup()
    const onSortingChange = vi.fn()

    render(
      <DataTable
        columns={columns}
        data={data}
        sorting={[]}
        onSortingChange={onSortingChange}
      />
    )

    await user.click(screen.getByRole("button", { name: "Name" }))

    // The page owns the state: callback fired, local order untouched.
    expect(onSortingChange).toHaveBeenCalledWith([{ id: "name", desc: false }])
    expect(nameColumnOrder()).toEqual(["Charlie", "Alice", "Bob"])
  })

  it("renders an export button that downloads the in-memory rows", async () => {
    const user = userEvent.setup()
    const createObjectURL = vi.fn(() => "blob:mock")
    const revokeObjectURL = vi.fn()
    vi.stubGlobal("URL", { createObjectURL, revokeObjectURL })

    render(<DataTable columns={columns} data={data} />)
    await user.click(screen.getByRole("button", { name: /export/i }))

    expect(createObjectURL).toHaveBeenCalledTimes(1)
    vi.unstubAllGlobals()
  })
})
