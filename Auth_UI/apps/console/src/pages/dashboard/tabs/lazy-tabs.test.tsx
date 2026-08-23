import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { TabFallback } from "./lazy-tabs"
import { TAB_LOADERS } from "./tab-loaders"

/**
 * Each dashboard tab is fetched on demand, so nothing checks that the module
 * and the export it names still line up until someone opens that tab. These
 * resolve every loader, which is the same guarantee the route tests give the
 * pages.
 */
describe("dashboard tab loaders", () => {
  const loaders = Object.entries(TAB_LOADERS)

  it("covers every tab the page offers", () => {
    expect(loaders.map(([name]) => name)).toEqual([
      "overview",
      "security",
      "people",
      "apps",
      "audit",
    ])
  })

  it.each(loaders)("the %s tab resolves to a component", async (_name, load) => {
    const resolved = await load()
    expect(resolved.default).toBeTypeOf("function")
  }, 20_000)
})

describe("TabFallback", () => {
  it("holds space while a tab arrives, so the page does not jump", () => {
    const { container } = render(<TabFallback />)

    // Two placeholders, sized - not an empty box that collapses the layout.
    const placeholders = container.querySelectorAll('[data-slot="skeleton"]')
    expect(placeholders).toHaveLength(2)
    expect(screen.queryByRole("alert")).toBeNull()
  })
})
