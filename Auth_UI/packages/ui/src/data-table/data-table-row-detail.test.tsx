import { describe, expect, it, vi } from "vitest"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"

import "@astoom/i18n"
import { DataTableRowDetail } from "./data-table-row-detail"

const row = {
  name: "Alice",
  email: "alice@example.com",
  createdAt: "2026-01-02T10:00:00Z",
  createdBy: "admin",
}

describe("DataTableRowDetail", () => {
  it("renders all record fields and groups audit fields under a legend", () => {
    render(<DataTableRowDetail row={row} open onOpenChange={() => {}} />)

    expect(screen.getByText("Alice")).toBeInTheDocument()
    expect(screen.getByText("alice@example.com")).toBeInTheDocument()
    // Audit group + its members.
    expect(screen.getByText("Audit Fields")).toBeInTheDocument()
    expect(screen.getByText("admin")).toBeInTheDocument()
  })

  it("renders nothing when there is no row", () => {
    render(<DataTableRowDetail row={null} open onOpenChange={() => {}} />)
    expect(screen.queryByText("Audit Fields")).not.toBeInTheDocument()
  })

  it("omits the Edit button when onEdit is not provided", () => {
    render(<DataTableRowDetail row={row} open onOpenChange={() => {}} />)
    expect(
      screen.queryByRole("button", { name: /edit/i })
    ).not.toBeInTheDocument()
  })

  it("hands the row back and closes when Edit is pressed", async () => {
    const onEdit = vi.fn()
    const onOpenChange = vi.fn()
    const user = userEvent.setup()

    render(
      <DataTableRowDetail
        row={row}
        open
        onOpenChange={onOpenChange}
        onEdit={onEdit}
      />
    )

    await user.click(screen.getByRole("button", { name: /edit/i }))
    expect(onEdit).toHaveBeenCalledWith(row)
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })
})
