import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { Pencil, Save, Trash2 } from "lucide-react"
import { describe, expect, it, vi } from "vitest"

import { PageActionSurface } from "./page-action-surface"

describe("PageActionSurface", () => {
  it("renders one contract as a desktop toolbar and a responsive menu", async () => {
    const edit = vi.fn()
    const remove = vi.fn()
    const { container } = render(
      <PageActionSurface
        label="Actions"
        actions={[
          {
            id: "save",
            label: "Save",
            icon: Save,
            pending: true,
            onAction: vi.fn(),
          },
          {
            id: "edit",
            label: "Edit",
            icon: Pencil,
            variant: "default",
            onAction: edit,
          },
          {
            id: "delete",
            label: "Delete",
            icon: Trash2,
            variant: "destructive",
            onAction: remove,
          },
        ]}
      />
    )
    const desktop = container.querySelector(
      '[data-slot="page-action-surface-desktop"]'
    )
    expect(desktop).not.toBeNull()
    const toolbar = within(desktop as HTMLElement)

    expect(toolbar.getByRole("button", { name: "Save" })).toBeDisabled()
    expect(toolbar.getByRole("button", { name: "Delete" })).toHaveAttribute(
      "data-variant",
      "destructive"
    )
    expect(desktop?.querySelector('[data-slot="separator"]')).not.toBeNull()
    await userEvent.click(toolbar.getByRole("button", { name: "Edit" }))
    expect(edit).toHaveBeenCalledTimes(1)

    await userEvent.click(screen.getByRole("button", { name: "Actions" }))
    const menu = screen.getByRole("menu")
    expect(within(menu).getByRole("menuitem", { name: "Save" })).toHaveAttribute(
      "data-disabled"
    )
    expect(
      within(menu).getByRole("menuitem", { name: "Delete" })
    ).toHaveAttribute("data-variant", "destructive")
    await userEvent.click(within(menu).getByRole("menuitem", { name: "Delete" }))
    expect(remove).toHaveBeenCalledTimes(1)
  })

  it("renders nothing when the permission-filtered contract is empty", () => {
    const { container } = render(
      <PageActionSurface label="Actions" actions={[]} />
    )
    expect(container).toBeEmptyDOMElement()
  })
})
