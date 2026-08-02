import { afterEach, describe, expect, it, vi } from "vitest"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ColumnDef } from "@tanstack/react-table"

import "@authsystem/i18n"
import { DataTable } from "./data-table"
import { __resetDataTableStorage, setDataTableScope } from "./storage"

// The layout store syncs to the API; these cases are about the local half.
vi.mock("@authsystem/api/ui-preferences", () => ({
  fetchUiPreferences: vi.fn(async () => ({})),
  putUiPreference: vi.fn(async () => true),
  deleteUiPreference: vi.fn(async () => true),
}))

const SCOPE = "11111111-1111-1111-1111-111111111111"

afterEach(() => {
  __resetDataTableStorage()
})

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

/** Visible header labels, in display order. */
function headerOrder(): string[] {
  return screen
    .getAllByRole("columnheader")
    .map((cell) => cell.textContent?.trim() ?? "")
}

/**
 * jsdom here ships no Storage implementation, so the persistence paths need a
 * stand-in before a table with a `tableId` is rendered.
 */
function stubLocalStorage(): Map<string, string> {
  const store = new Map<string, string>()
  const mock: Storage = {
    getItem: (key) => store.get(key) ?? null,
    setItem: (key, value) => {
      store.set(key, String(value))
    },
    removeItem: (key) => {
      store.delete(key)
    },
    clear: () => store.clear(),
    key: (index) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size
    },
  }
  Object.defineProperty(window, "localStorage", {
    value: mock,
    configurable: true,
    writable: true,
  })
  // Layouts are scoped to a user; without one nothing persists at all.
  __resetDataTableStorage()
  setDataTableScope(SCOPE)
  return store
}

/** The single stored document for one table, under the active scope. */
function storedLayout(store: Map<string, string>, tableId: string): unknown {
  return JSON.parse(store.get(`dt:${SCOPE}:${tableId}`) ?? "null")
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

  it("lets the keyboard drive the column resize handle", async () => {
    const user = userEvent.setup()
    render(<DataTable columns={columns} data={data} />)

    const handle = screen.getAllByRole("separator")[0]
    expect(handle).toHaveAttribute("tabindex", "0")
    expect(handle).toHaveAccessibleName(/name/i)

    const head = screen.getAllByRole("columnheader")[0]
    expect(head.style.width).toBe("")

    handle.focus()
    await user.keyboard("{ArrowRight}")
    expect(head.style.width).not.toBe("")

    await user.keyboard("{Home}")
    expect(head.style.width).toBe("")
  })

  it("persists only the visibility choices that differ from the default", async () => {
    const store = stubLocalStorage()
    const user = userEvent.setup()
    // Two fields beyond the curated columns, so both arrive as auto-discovered
    // columns that are hidden by default.
    const rows = [{ name: "Charlie", age: 30, email: "c@x.com", city: "Cairo" }]

    render(
      <DataTable
        columns={columns as ColumnDef<(typeof rows)[number], unknown>[]}
        data={rows}
        tableId="prune"
      />
    )

    await user.click(screen.getByRole("button", { name: /columns/i }))
    await user.click(screen.getByRole("menuitemcheckbox", { name: "Email" }))

    // "city" stays hidden, which is already the default, so it must not be
    // written — otherwise the blob grows by one entry per API field forever.
    expect(storedLayout(store, "prune")).toMatchObject({ cols: { email: true } })
  })

  it("reorders columns from the menu, announces it and persists the order", async () => {
    const store = stubLocalStorage()
    const user = userEvent.setup()
    render(<DataTable columns={columns} data={data} tableId="order" />)

    expect(headerOrder()).toEqual(["Name", "Age"])

    await user.click(screen.getByRole("button", { name: /columns/i }))
    await user.click(screen.getByRole("button", { name: /move name later/i }))
    // The open menu marks the rest of the page aria-hidden, so the grid is only
    // queryable once it closes.
    await user.keyboard("{Escape}")

    expect(headerOrder()).toEqual(["Age", "Name"])
    expect(storedLayout(store, "order")).toMatchObject({
      order: ["age", "name"],
    })
    // The move has no visual cue a screen reader can use, so it is narrated.
    expect(
      screen.getByText("Name moved to position 2 of 2")
    ).toBeInTheDocument()
  })

  it("restores a persisted column order on mount", () => {
    const store = stubLocalStorage()
    store.set(
      `dt:${SCOPE}:restore`,
      JSON.stringify({ order: ["age", "name"] })
    )

    render(<DataTable columns={columns} data={data} tableId="restore" />)

    expect(headerOrder()).toEqual(["Age", "Name"])
  })

  it("does not apply another user's stored layout", () => {
    const store = stubLocalStorage()
    store.set(
      "dt:22222222-2222-2222-2222-222222222222:scoped",
      JSON.stringify({ order: ["age", "name"] })
    )

    render(<DataTable columns={columns} data={data} tableId="scoped" />)

    expect(headerOrder()).toEqual(["Name", "Age"])
  })

  it("keeps the actions column last whatever the persisted order says", () => {
    const store = stubLocalStorage()
    store.set(
      `dt:${SCOPE}:pinned`,
      JSON.stringify({ order: ["actions", "age", "name"] })
    )
    const withActions: ColumnDef<Person, unknown>[] = [
      ...columns,
      { id: "actions", enableSorting: false, enableHiding: false, header: () => null },
    ]

    render(<DataTable columns={withActions} data={data} tableId="pinned" />)

    expect(headerOrder()).toEqual(["Age", "Name", ""])
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
