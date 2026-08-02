import { describe, expect, it } from "vitest"

import { matchRanges } from "./match-ranges"

describe("matchRanges", () => {
  it("finds every occurrence of every word typed", () => {
    expect(matchRanges("Session idle timeout", "session timeout")).toEqual([
      [0, 7],
      [13, 20],
    ])
  })

  it("matches regardless of case, without changing what is displayed", () => {
    // Ranges index the original string, so the slice keeps its own casing.
    expect(matchRanges("Argon2 Memory Size", "memory")).toEqual([[7, 13]])
  })

  it("merges overlapping words into one run", () => {
    expect(matchRanges("password", "pass sword ssw")).toEqual([[0, 8]])
  })

  it("treats regex metacharacters as literal text", () => {
    // The query is user input. A RegExp would throw on "(" and over-match ".".
    expect(() => matchRanges("Rate limiting (per IP)", "(per")).not.toThrow()
    expect(matchRanges("Rate limiting (per IP)", "(per")).toEqual([[14, 18]])
    expect(matchRanges("abc", ".")).toEqual([])
  })

  it("returns nothing for a blank query", () => {
    expect(matchRanges("anything", "   ")).toEqual([])
  })

  it("matches Arabic the same way", () => {
    expect(matchRanges("مدة الجلسة", "الجلسة")).toEqual([[4, 10]])
  })
})
