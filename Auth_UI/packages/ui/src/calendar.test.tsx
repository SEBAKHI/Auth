import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { Calendar } from "./calendar"

/**
 * The selected day must stay legible while the pointer is on it.
 *
 * jsdom applies no stylesheet, so this asserts the *mechanism*. The ghost variant's
 * `dark:hover:bg-muted/50` outranks `data-[selected]:bg-primary` — Tailwind emits
 * `dark:` variants in a later layer than `data-*` ones — so on hover the background
 * legitimately goes grey. The bug was that the text stayed `primary-foreground`,
 * near-black in dark mode. `dark:hover:text-foreground`, which is upstream's own fix,
 * pins the text in that same layer so it stays light.
 */
describe("Calendar day button", () => {
  it("keeps upstream's ghost treatment plus the dark-mode hover text pin", () => {
    render(
      <Calendar
        mode="single"
        selected={new Date(2026, 4, 14)}
        defaultMonth={new Date(2026, 4, 1)}
      />
    )

    const selected = screen.getByRole("button", { name: /May 14th, 2026/ })
    const cls = selected.className

    expect(selected).toHaveAttribute("data-selected-single", "true")
    // Upstream's selected treatment, unchanged.
    expect(cls).toContain("data-[selected-single=true]:bg-primary")
    expect(cls).toContain("data-[selected-single=true]:text-primary-foreground")
    // Still the ghost variant — no bespoke variant swap.
    expect(cls).toContain("hover:bg-muted")
    expect(cls).toContain("dark:hover:bg-muted/50")
    // The one utility that makes the hovered selection readable in dark mode.
    expect(cls).toContain("dark:hover:text-foreground")
  })

  it("applies the same treatment to every day, selected or not", () => {
    render(
      <Calendar
        mode="single"
        selected={new Date(2026, 4, 14)}
        defaultMonth={new Date(2026, 4, 1)}
      />
    )

    // The day button is uniform: selection is expressed by data attributes, not by
    // swapping the button's variant, so an unselected day carries the same classes.
    const other = screen.getByRole("button", { name: /May 20th, 2026/ })
    expect(other.className).toContain("dark:hover:text-foreground")
    expect(other.className).toContain("hover:bg-muted")
  })
})
