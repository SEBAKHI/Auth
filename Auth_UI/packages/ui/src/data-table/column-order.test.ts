import { describe, expect, it } from "vitest"

import {
  columnPosition,
  moveColumn,
  reorderColumn,
  resolveColumnOrder,
} from "./column-order"

describe("resolveColumnOrder", () => {
  it("applies a stored order", () => {
    expect(resolveColumnOrder(["a", "b", "c"], ["c", "a", "b"])).toEqual([
      "c",
      "a",
      "b",
    ])
  })

  it("falls back to the natural order when nothing is stored", () => {
    expect(resolveColumnOrder(["a", "b", "c"], [])).toEqual(["a", "b", "c"])
  })

  it("ignores stored ids the table no longer has", () => {
    expect(resolveColumnOrder(["a", "b"], ["gone", "b", "a"])).toEqual([
      "b",
      "a",
    ])
  })

  it("leaves a newly discovered column in its natural slot", () => {
    // "fresh" sits between a and b; the stored order only rearranges a and b
    // among the slots they already hold.
    expect(
      resolveColumnOrder(["a", "fresh", "b"], ["b", "a"])
    ).toEqual(["b", "fresh", "a"])
  })

  it("forces the actions column last, wherever it was stored", () => {
    expect(
      resolveColumnOrder(["a", "b", "actions"], ["actions", "b", "a"])
    ).toEqual(["b", "a", "actions"])
  })

  it("keeps the actions column last when nothing is stored", () => {
    expect(resolveColumnOrder(["a", "actions", "b"], [])).toEqual([
      "a",
      "b",
      "actions",
    ])
  })
})

describe("moveColumn", () => {
  it("moves a column one slot later", () => {
    expect(moveColumn(["a", "b", "c"], "a", 1)).toEqual(["b", "a", "c"])
  })

  it("moves a column one slot earlier", () => {
    expect(moveColumn(["a", "b", "c"], "c", -1)).toEqual(["a", "c", "b"])
  })

  it("is a no-op past either end", () => {
    expect(moveColumn(["a", "b"], "a", -1)).toEqual(["a", "b"])
    expect(moveColumn(["a", "b"], "b", 1)).toEqual(["a", "b"])
  })

  it("is a no-op for an unknown column", () => {
    expect(moveColumn(["a", "b"], "nope", 1)).toEqual(["a", "b"])
  })

  it("never moves a column past the pinned actions column", () => {
    expect(moveColumn(["a", "b", "actions"], "b", 1)).toEqual([
      "a",
      "b",
      "actions",
    ])
  })
})

describe("reorderColumn", () => {
  it("drops the dragged column onto the target's slot", () => {
    expect(reorderColumn(["a", "b", "c"], "a", "c")).toEqual(["b", "c", "a"])
    expect(reorderColumn(["a", "b", "c"], "c", "a")).toEqual(["c", "a", "b"])
  })

  it("is a no-op when source and target match, or either is unknown", () => {
    expect(reorderColumn(["a", "b"], "a", "a")).toEqual(["a", "b"])
    expect(reorderColumn(["a", "b"], "a", "nope")).toEqual(["a", "b"])
  })

  it("keeps actions last and refuses it as a target", () => {
    expect(reorderColumn(["a", "b", "actions"], "a", "actions")).toEqual([
      "a",
      "b",
      "actions",
    ])
  })
})

describe("columnPosition", () => {
  it("reports a 1-based position among the movable columns", () => {
    expect(columnPosition(["a", "b", "actions"], "b")).toEqual({
      position: 2,
      total: 2,
    })
  })
})
