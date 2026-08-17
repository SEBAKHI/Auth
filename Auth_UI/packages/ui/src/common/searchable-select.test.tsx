import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { SearchableSelect } from "./searchable-select"

/**
 * The bug this pins is invisible in the DOM and obvious on screen: a permission
 * code ending in a neutral character is REORDERED by the bidirectional
 * algorithm inside a right-to-left page, so `org:members:*` is painted as
 * `*:org:members` — the same characters reading as a different permission.
 *
 * The first fix set `dir="ltr"` on the whole option and took the column's
 * alignment with it, moving the list to the other side of the popover. So both
 * halves are asserted: the label's run is isolated, and nothing declares a
 * direction on the row that would drag the alignment along.
 */
describe("SearchableSelect", () => {
  const options = [{ id: "1", label: "org:members:*", description: "Members" }]

  it("isolates the label when asked", () => {
    render(
      <SearchableSelect
        value="1"
        options={options}
        onChange={() => {}}
        ltrLabel
      />
    )

    const isolated = screen.getByText("org:members:*")
    expect(isolated.tagName).toBe("BDI")
    expect(isolated.getAttribute("dir")).toBe("ltr")
  })

  it("leaves the label alone by default, for names that follow the page", () => {
    render(
      <SearchableSelect value="1" options={options} onChange={() => {}} />
    )

    expect(screen.getByText("org:members:*").tagName).not.toBe("BDI")
  })

  it("never sets a direction outside the label", () => {
    const { container } = render(
      <SearchableSelect
        value="1"
        options={options}
        onChange={() => {}}
        ltrLabel
      />
    )

    // Alignment follows the page. Anything carrying `dir` other than the bdi
    // would pull the column to the opposite side of the interface.
    const directed = [...container.querySelectorAll("[dir]")]
    expect(directed.every((node) => node.tagName === "BDI")).toBe(true)
  })
})
