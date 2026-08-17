import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import { SearchableSelect } from "./searchable-select"

/**
 * Direction here follows the CONTENT, not the page.
 *
 * Left alone inside a right-to-left page, a code ending in a neutral character
 * has that character resolved to the paragraph direction, so `org:members:*` is
 * painted as `*:org:members` — the same characters reading as a different
 * permission. Measured in a browser rather than deduced: with the run declared
 * left-to-right the visual order is `apikeys:*`, without it `*:apikeys`.
 *
 * Isolating the run WITHOUT moving the column was tried and rejected in
 * between. It fixes the character order and leaves the code right-aligned, so
 * the trailing `*` sits against the right edge and is the first glyph an Arabic
 * reader meets — correct, and unreadable.
 *
 * Every case opens the popover first. An earlier version of this file did not,
 * so its assertions landed on the trigger button and passed while saying
 * nothing about the list they were written for.
 */
describe("SearchableSelect", () => {
  const options = [{ id: "1", label: "org:members:*", description: "Members" }]

  async function openList(ltr: boolean) {
    const user = userEvent.setup()
    const view = render(
      <SearchableSelect
        value={undefined}
        options={options}
        onChange={() => {}}
        ltr={ltr}
      />
    )
    await user.click(screen.getByRole("combobox"))
    return view
  }

  /**
   * The wrapper this component owns, holding the label and description. Asked
   * for by name rather than with `closest("[dir]")`, which walks straight past
   * it into Radix's own root — that carries a dir of its own and answered the
   * question about somebody else's element.
   */
  const optionContent = (label: HTMLElement) => label.parentElement!

  it("declares left-to-right on the option when the content is Latin", async () => {
    await openList(true)

    const label = await screen.findByText("org:members:*")
    expect(optionContent(label).getAttribute("dir")).toBe("ltr")
  })

  it("leaves direction to the page otherwise", async () => {
    // Role labels carry Arabic and belong on the page's own side.
    await openList(false)

    const label = await screen.findByText("org:members:*")
    expect(optionContent(label).hasAttribute("dir")).toBe(false)
  })

  it("wraps the description instead of truncating it", async () => {
    // The description is the only thing telling an operator what
    // users:manage-roles actually does, and the longest ones — the org roles —
    // were the ones running out past the popover edge.
    await openList(true)

    const description = await screen.findByText("Members")
    expect(description.className).not.toContain("truncate")
    expect(description.className).toContain("break-words")
  })
})
