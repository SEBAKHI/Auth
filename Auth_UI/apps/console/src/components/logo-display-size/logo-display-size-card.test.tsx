import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

import { LogoDisplaySizeCard } from "./logo-display-size-card"

/**
 * The logo's display size (owner decisions D5/D6): a height outside the upload dialog, saved
 * alone without choosing a file, previewed at real size in light and dark, and resettable to the
 * owner's default. Breaks: send anything but the height; save on every drag; drop the reset; draw
 * the dark preview with the light logo.
 */

function renderCard(props: Partial<React.ComponentProps<typeof LogoDisplaySizeCard>> = {}) {
  const save = vi.fn((px: number | null) => Promise.resolve(px))
  const onUnsavedChange = vi.fn()
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <LogoDisplaySizeCard
        owner="platform"
        storedPx={null}
        name="Acme"
        lightSrc="light.webp"
        darkSrc="dark.webp"
        canEdit
        save={save}
        onUnsavedChange={onUnsavedChange}
        {...props}
      />
    </QueryClientProvider>
  )
  return { save, onUnsavedChange }
}

const slider = () => screen.getByRole("slider", { name: "Height" })
const button = (name: string | RegExp) => screen.getByRole("button", { name })
const previews = () =>
  Array.from(document.querySelectorAll<HTMLElement>('[data-slot="logo-display-size-preview"]'))

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

describe("LogoDisplaySizeCard", () => {
  it("starts at the owner's default, marked as the default, with nothing to save", () => {
    renderCard()

    expect(slider()).toHaveAttribute("aria-valuenow", "60")
    expect(slider()).toHaveAttribute("aria-valuemin", "24")
    expect(slider()).toHaveAttribute("aria-valuemax", "120")
    expect(screen.getByText("60 px")).toBeInTheDocument()
    expect(screen.getByText("Default")).toBeInTheDocument()
    expect(button("Save")).toBeDisabled()
    expect(button("Use the default (60 px)")).toBeDisabled()
  })

  it("previews the logo at the dragged height in each mode, with that mode's logo", async () => {
    const user = userEvent.setup()
    renderCard()

    slider().focus()
    await user.keyboard("{ArrowRight}{ArrowRight}")

    const [light, dark] = previews()
    expect(light).toHaveAttribute("data-mode", "light")
    expect(light).toHaveClass("light")
    expect(dark).toHaveClass("dark")
    const lightLogo = within(light!).getByRole("img", { name: "Acme" })
    const darkLogo = within(dark!).getByRole("img", { name: "Acme" })
    expect(lightLogo).toHaveAttribute("src", "light.webp")
    expect(darkLogo).toHaveAttribute("src", "dark.webp")
    expect(lightLogo.style.getPropertyValue("--logo-h")).toBe("62px")
    expect(darkLogo.style.getPropertyValue("--logo-h")).toBe("62px")
    expect(screen.queryByText("Default")).toBeNull()
  })

  it("saves the height alone, only on Save, and reports the draft as unsaved meanwhile", async () => {
    const user = userEvent.setup()
    const { save, onUnsavedChange } = renderCard()

    slider().focus()
    await user.keyboard("{End}")
    expect(save).not.toHaveBeenCalled()
    expect(onUnsavedChange).toHaveBeenLastCalledWith({ isDirty: true, isSaving: false })

    await user.click(button("Save"))

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1))
    expect(save).toHaveBeenCalledWith(120)
    await waitFor(() => expect(onUnsavedChange).toHaveBeenLastCalledWith({ isDirty: false, isSaving: false }))
    expect(button("Save")).toBeDisabled()
  })

  it("goes back to the default with null, not with the default's number", async () => {
    const user = userEvent.setup()
    const { save } = renderCard({ storedPx: 100 })

    expect(slider()).toHaveAttribute("aria-valuenow", "100")
    await user.click(button("Use the default (60 px)"))
    expect(slider()).toHaveAttribute("aria-valuenow", "60")
    await user.click(button("Save"))

    await waitFor(() => expect(save).toHaveBeenCalledWith(null))
    expect(await screen.findByText("Default")).toBeInTheDocument()
  })

  it("reverts a draft to what is saved", async () => {
    const user = userEvent.setup()
    renderCard({ storedPx: 80 })

    slider().focus()
    await user.keyboard("{Home}")
    expect(slider()).toHaveAttribute("aria-valuenow", "24")
    await user.click(button("Discard changes"))

    expect(slider()).toHaveAttribute("aria-valuenow", "80")
    expect(button("Save")).toBeDisabled()
  })

  it("keeps the draft when the save fails, so it can be sent again", async () => {
    const user = userEvent.setup()
    const save = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce(90)
    renderCard({ save })

    slider().focus()
    for (let step = 0; step < 30; step++) await user.keyboard("{ArrowUp}")
    expect(slider()).toHaveAttribute("aria-valuenow", "90")
    await user.click(button("Save"))

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(button("Save")).toBeEnabled())
    expect(slider()).toHaveAttribute("aria-valuenow", "90")

    await user.click(button("Save"))
    await waitFor(() => expect(save).toHaveBeenCalledTimes(2))
    expect(save).toHaveBeenLastCalledWith(90)
    await waitFor(() => expect(button("Save")).toBeDisabled())
  })

  it("starts an application's logo at 48px and says when there is no logo to preview", () => {
    renderCard({ owner: "application", storedPx: null, lightSrc: null, darkSrc: null })

    expect(slider()).toHaveAttribute("aria-valuenow", "48")
    expect(button("Use the default (48 px)")).toBeDisabled()
    expect(screen.getByText(/No logo uploaded: the sign-in pages show the name instead\./)).toBeInTheDocument()
    for (const preview of previews()) expect(within(preview).getByText("Acme")).toBeInTheDocument()
  })

  it("shows the size without changing it for someone who may not edit", () => {
    renderCard({ owner: "application", storedPx: 72, canEdit: false })

    expect(slider()).toHaveAttribute("aria-valuenow", "72")
    expect(slider()).toHaveAttribute("data-disabled")
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull()
  })
})
