import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { Pencil, Save, Trash2 } from "lucide-react"
import { describe, expect, it, vi } from "vitest"

import { PageActionSurface } from "./page-action-surface"

const SAVE = { id: "save", label: "Save", icon: Save } as const
const EDIT = { id: "edit", label: "Edit", icon: Pencil } as const
const DELETE = { id: "delete", label: "Delete", icon: Trash2 } as const

describe("PageActionSurface", () => {
  it("keeps the primary action out front and the rest in the named menu", async () => {
    const edit = vi.fn()
    const remove = vi.fn()
    render(
      <PageActionSurface
        label="Actions"
        actions={[
          { ...SAVE, pending: true, onAction: vi.fn() },
          { ...EDIT, variant: "default", onAction: edit },
          { ...DELETE, variant: "destructive", onAction: remove },
        ]}
      />
    )

    // Two controls, whatever the contract holds: the primary and the menu.
    // Every other action is one click away, never a button of its own.
    expect(screen.getAllByRole("button")).toHaveLength(2)

    const primary = screen.getByRole("button", { name: "Edit" })
    expect(primary).toHaveAttribute("data-variant", "default")
    await userEvent.click(primary)
    expect(edit).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole("button", { name: "Actions" }))
    const menu = screen.getByRole("menu")

    // The primary is not repeated inside the menu it sits beside.
    expect(within(menu).queryByRole("menuitem", { name: "Edit" })).toBeNull()

    expect(within(menu).getByRole("menuitem", { name: "Save" })).toHaveAttribute(
      "data-disabled"
    )
    expect(
      within(menu).getByRole("menuitem", { name: "Delete" })
    ).toHaveAttribute("data-variant", "destructive")
    expect(
      menu.querySelector('[data-slot="dropdown-menu-separator"]'),
      "danger is separated from the ordinary actions"
    ).not.toBeNull()

    await userEvent.click(within(menu).getByRole("menuitem", { name: "Delete" }))
    expect(remove).toHaveBeenCalledTimes(1)
  })

  it.each([
    ["disabled", { disabled: true }],
    // Pending closes the same door for a different reason: the action is
    // already in flight, so a second press must not start it again.
    ["pending", { pending: true }],
  ])(
    "holds the promoted action shut while it is %s",
    (_state, guard) => {
      const act = vi.fn()
      render(
        <PageActionSurface
          label="Actions"
          actions={[
            { ...EDIT, variant: "default", ...guard, onAction: act },
            { ...DELETE, variant: "destructive", onAction: vi.fn() },
          ]}
        />
      )

      // Promoting an action must not drop the guards it declared. The template
      // editor's primary is Publish, gated on `isDirty` and on the mutation
      // being in flight - lose either and it ships the last SAVED draft while
      // unsaved edits sit on screen.
      expect(screen.getByRole("button", { name: "Edit" })).toBeDisabled()
    }
  )

  it("offers no menu when the primary action is the whole contract", () => {
    render(
      <PageActionSurface
        label="Actions"
        actions={[{ ...EDIT, variant: "default", onAction: vi.fn() }]}
      />
    )

    expect(screen.getByRole("button", { name: "Edit" })).toBeVisible()
    expect(screen.queryByRole("button", { name: "Actions" })).toBeNull()
  })

  it("offers only the menu when no action claims the primary slot", () => {
    render(
      <PageActionSurface
        label="Actions"
        actions={[
          { ...SAVE, onAction: vi.fn() },
          { ...DELETE, variant: "destructive", onAction: vi.fn() },
        ]}
      />
    )

    expect(screen.getAllByRole("button")).toHaveLength(1)
    expect(screen.getByRole("button", { name: "Actions" })).toBeVisible()
  })

  it("renders nothing when the permission-filtered contract is empty", () => {
    const { container } = render(
      <PageActionSurface label="Actions" actions={[]} />
    )
    expect(container).toBeEmptyDOMElement()
  })
})
