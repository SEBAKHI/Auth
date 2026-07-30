import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { Calendar } from "./calendar"

/**
 * The selected day must stay legible while the pointer is on it.
 *
 * jsdom does not apply a stylesheet, so this asserts the *mechanism* instead: the
 * selected day is rendered with the Button `default` variant, whose hover only dims
 * the background and never changes the text colour. The regression this guards
 * against was the day being rendered `ghost`, where this project's
 * `dark:hover:bg-muted/50` outranks `data-[selected]:bg-primary` — Tailwind emits
 * `dark:` variants in a later layer than `data-*` ones — leaving near-black text on
 * a dark grey background.
 */
describe("Calendar day button", () => {
  it("renders the selected day with the primary (not ghost) treatment", () => {
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
    // The `default` variant's own hover: dims the background, keeps the text token.
    expect(cls).toContain("bg-primary")
    expect(cls).toContain("text-primary-foreground")
    expect(cls).toContain("hover:bg-primary/80")
    // The ghost hover pair is what made the number unreadable; it must be absent.
    expect(cls).not.toContain("dark:hover:bg-muted/50")
    expect(cls).not.toContain("hover:text-foreground")
  })

  it("leaves an unselected day on the ghost treatment", () => {
    render(
      <Calendar
        mode="single"
        selected={new Date(2026, 4, 14)}
        defaultMonth={new Date(2026, 4, 1)}
      />
    )

    const other = screen.getByRole("button", { name: /May 20th, 2026/ })
    expect(other.className).toContain("hover:bg-muted")
    expect(other.className).not.toContain("bg-primary")
    expect(other.className).not.toContain("text-primary-foreground")
  })
})
