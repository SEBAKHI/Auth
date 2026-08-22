import { describe, expect, it, vi } from "vitest"

import {
  collectAllPages,
  SORT_ASC,
  SORT_DESC,
  toNumber,
  toSortParams,
  unwrap,
} from "./helpers"

describe("API helpers", () => {
  // A gateway 502 serving an HTML page leaves openapi-fetch with no `error` to
  // report. Passing that through returned `undefined` as data, and the list
  // that received it rendered "no results" for a broken request.
  it("rejects a failed response whose body could not be read as an error", async () => {
    const response = { ok: false, status: 502, statusText: "Bad Gateway" }
    await expect(
      unwrap(Promise.resolve({ data: undefined, response: response as Response }))
    ).rejects.toMatchObject({ status: 502 })
  })

  it("passes a successful response through untouched", async () => {
    const response = { ok: true, status: 200, statusText: "OK" }
    await expect(
      unwrap(Promise.resolve({ data: 7, response: response as Response }))
    ).resolves.toBe(7)
  })

  it("unwraps data and rejects API errors", async () => {
    await expect(unwrap(Promise.resolve({ data: 7 }))).resolves.toBe(7)
    const error = { title: "forbidden" }
    await expect(unwrap(Promise.resolve({ error }))).rejects.toBe(error)
  })

  it("normalizes numeric and sorting parameters", () => {
    expect(toNumber(null)).toBe(0)
    expect(toNumber(undefined)).toBe(0)
    expect(toNumber("12")).toBe(12)
    expect(toNumber(4)).toBe(4)
    expect(toSortParams([])).toEqual({})
    expect(toSortParams([{ id: "name", desc: false }])).toEqual({
      sortBy: "name",
      sortDirection: SORT_ASC,
    })
    expect(toSortParams([{ id: "createdAt", desc: true }])).toEqual({
      sortBy: "createdAt",
      sortDirection: SORT_DESC,
    })
  })

  it("collects pages until the reported total", async () => {
    const fetchPage = vi
      .fn()
      .mockResolvedValueOnce({ items: [1, 2], totalCount: 3 })
      .mockResolvedValueOnce({ items: [3], totalCount: 3 })

    await expect(
      collectAllPages(fetchPage, { pageSize: 2, maxRows: 10 })
    ).resolves.toEqual([1, 2, 3])
    expect(fetchPage).toHaveBeenNthCalledWith(1, 1, 2)
    expect(fetchPage).toHaveBeenNthCalledWith(2, 2, 2)
  })

  it("stops on an empty page and at the safety cap", async () => {
    const empty = vi.fn().mockResolvedValue({ items: [], totalCount: 100 })
    await expect(collectAllPages(empty, { pageSize: 2 })).resolves.toEqual([])

    const capped = vi.fn().mockResolvedValue({ items: [1, 2], totalCount: 100 })
    await expect(
      collectAllPages(capped, { pageSize: 2, maxRows: 2 })
    ).resolves.toEqual([1, 2])
    expect(capped).toHaveBeenCalledOnce()
  })
})
