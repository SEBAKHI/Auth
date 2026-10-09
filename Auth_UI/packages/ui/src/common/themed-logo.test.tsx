import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const theme = vi.hoisted(() => ({ resolved: "light" as "light" | "dark" }))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useResolvedTheme: () => theme.resolved,
}))
// The dialog's slots upload through the real AvatarMenu; nothing here uploads.
vi.mock("@authsystem/api/use-logo", () => ({
  useLogo: () => ({ onChange: vi.fn(), onRemove: vi.fn(), pending: false }),
}))

import { ThemedLogo } from "./themed-logo"

function renderLogo(canEdit = true) {
  return render(
    <ThemedLogo
      name="CMS"
      lightSrc="https://cdn.test/light.webp"
      darkSrc="https://cdn.test/dark.webp"
      canEdit={canEdit}
      persistLight={vi.fn()}
      persistDark={vi.fn()}
      invalidate={vi.fn()}
      successMessage="Saved"
      dialogTitle="Application logo"
      dialogDescription="Pick a logo for each mode."
    />
  )
}

describe("ThemedLogo", () => {
  it("shows one circle on the page, for an editor too", () => {
    renderLogo()

    // One trigger: the page carries a single mark, not one per mode.
    expect(screen.getAllByRole("button", { name: /avatar/i })).toHaveLength(1)
  })

  it("opens one circle per mode from Change", async () => {
    const user = userEvent.setup()
    renderLogo()

    await user.click(screen.getByRole("button", { name: /avatar/i }))
    await user.click(await screen.findByRole("menuitem", { name: "Change" }))

    const dialog = await screen.findByRole("dialog", { name: "Application logo" })
    expect(within(dialog).getByText("Pick a logo for each mode.")).toBeTruthy()
    expect(within(dialog).getByText("Light mode")).toBeTruthy()
    expect(within(dialog).getByText("Dark mode")).toBeTruthy()
    expect(within(dialog).getAllByRole("button", { name: /avatar/i })).toHaveLength(2)
    // Each slot is painted in its own mode, whatever mode the page is in: a
    // white wordmark meant for dark pages is invisible on a light tile.
    expect(dialog.querySelector('[data-mode="light"]')?.classList.contains("light")).toBe(true)
    expect(dialog.querySelector('[data-mode="dark"]')?.classList.contains("dark")).toBe(true)
  })

  it("offers no menu to a reader", () => {
    renderLogo(false)

    expect(screen.queryByRole("button", { name: /avatar/i })).toBeNull()
  })
})
