import { describe, expect, it } from "vitest"
import type { Row } from "@tanstack/react-table"

import { facetedFilterFn } from "./filters"

function rowWith(value: unknown): Row<unknown> {
  return { getValue: () => value } as unknown as Row<unknown>
}

const noop = () => {}

describe("facetedFilterFn", () => {
  it("keeps every row when no value is selected", () => {
    expect(facetedFilterFn(rowWith("active"), "status", [], noop)).toBe(true)
    expect(facetedFilterFn(rowWith("active"), "status", undefined, noop)).toBe(true)
  })

  it("keeps rows whose value is among the selected set", () => {
    expect(
      facetedFilterFn(rowWith("active"), "status", ["active", "locked"], noop)
    ).toBe(true)
    expect(facetedFilterFn(rowWith("inactive"), "status", ["active"], noop)).toBe(
      false
    )
  })

  it("coerces non-string values before comparing", () => {
    expect(facetedFilterFn(rowWith(true), "isSystem", ["true"], noop)).toBe(true)
    expect(facetedFilterFn(rowWith(2), "count", ["1"], noop)).toBe(false)
  })
})
