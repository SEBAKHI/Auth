import { act, renderHook } from "@testing-library/react"
import { beforeEach, describe, expect, it } from "vitest"

import { useRecentSearches } from "./use-recent-searches"

const key = (userId: string) =>
  `authsystem.settingsSearch.recent.${userId}`

describe("useRecentSearches", () => {
  beforeEach(() => localStorage.clear())

  it("keeps the five newest unique entries per account", () => {
    const { result } = renderHook(() => useRecentSearches("account-a"))

    for (let index = 0; index < 6; index += 1) {
      act(() =>
        result.current.remember({
          id: `entry-${index}`,
          route: `/entry-${index}`,
        })
      )
    }
    act(() =>
      result.current.remember({ id: "entry-3", route: "/entry-3-new" })
    )

    expect(result.current.recent).toHaveLength(5)
    expect(result.current.recent[0]).toEqual({
      id: "entry-3",
      route: "/entry-3-new",
    })
    expect(result.current.recent.map((entry) => entry.id)).not.toContain(
      "entry-0"
    )
    expect(localStorage.getItem(key("account-a"))).not.toBeNull()
  })

  it("isolates accounts and clears only the active account", () => {
    localStorage.setItem(
      key("account-b"),
      JSON.stringify([{ id: "other", route: "/other" }])
    )
    const { result } = renderHook(() => useRecentSearches("account-a"))

    act(() => result.current.remember({ id: "mine", route: "/mine" }))
    act(() => result.current.clear())

    expect(result.current.recent).toEqual([])
    expect(JSON.parse(localStorage.getItem(key("account-b")) ?? "[]")).toEqual([
      { id: "other", route: "/other" },
    ])
  })

  it("rejects malformed storage entries without throwing", () => {
    localStorage.setItem(
      key("account-a"),
      JSON.stringify([
        null,
        { id: 1, route: "/numeric" },
        { id: "missing-route" },
        { id: "valid", route: "/valid" },
      ])
    )

    const { result } = renderHook(() => useRecentSearches("account-a"))

    expect(result.current.recent).toEqual([{ id: "valid", route: "/valid" }])
  })

  it("falls back to an empty list for invalid JSON", () => {
    localStorage.setItem(key("account-a"), "not-json")

    const { result } = renderHook(() => useRecentSearches("account-a"))

    expect(result.current.recent).toEqual([])
  })
})
