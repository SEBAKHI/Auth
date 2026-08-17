import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import { SearchableSelect } from "./searchable-select"

/**
 * Direction is the page's to decide, and this component must not decide it.
 *
 * A permission code is a sequence of directional islands separated by neutral
 * characters, not an opaque token. In `org:members:*` the trailing `:` and `*`
 * neighbour no island on their right, so the bidirectional algorithm resolves
 * them to the paragraph direction: on an Arabic page they move left of the
 * Latin islands and the line paints as `*:org:members`. A reader of that page
 * scans islands right to left — `org:members`, then `:`, then `*` — and reads
 * the code in order, in their own reading direction. On an English page the
 * same markup paints and reads `org:members:*`.
 *
 * Three earlier versions forced `dir="ltr"` here, on the inherited assumption
 * that identifiers are always left-to-right. That convention comes from
 * interfaces that only ever had one direction. Forcing it puts the `*` at the
 * edge an Arabic reader starts from, which is the one arrangement that does
 * read wrong — and this test exists so the assumption cannot come back.
 */
describe("SearchableSelect", () => {
  const options = [{ id: "1", label: "org:members:*", description: "Members" }]

  async function openList() {
    const user = userEvent.setup()
    const view = render(
      <SearchableSelect value={undefined} options={options} onChange={() => {}} />
    )
    await user.click(screen.getByRole("combobox"))
    return view
  }

  it("declares no direction of its own", async () => {
    const { baseElement } = await openList()

    // Scoped to what this component renders. Radix's own roots carry a dir,
    // which is theirs to set and follows the DirectionProvider.
    const label = await screen.findByText("org:members:*")
    const option = label.closest("[cmdk-item]") ?? label.parentElement!.parentElement!

    expect(option.querySelector("[dir]")).toBeNull()
    expect(baseElement.querySelector("bdi")).toBeNull()
  })

  it("aligns to the start of the line, whichever side that is", async () => {
    // `text-start` resolves to the right on an RTL page and the left on an LTR
    // one, so one rule serves both. A hardcoded text-left or text-right would
    // be correct in exactly one language.
    await openList()

    const label = await screen.findByText("org:members:*")
    expect(label.parentElement!.className).toContain("text-start")
  })

  it("wraps the description instead of truncating it", async () => {
    // The description is the only thing telling an operator what
    // users:manage-roles actually does, and the longest ones — the org roles —
    // were the ones running out past the popover edge.
    await openList()

    const description = await screen.findByText("Members")
    expect(description.className).not.toContain("truncate")
    expect(description.className).toContain("break-words")
  })
})
