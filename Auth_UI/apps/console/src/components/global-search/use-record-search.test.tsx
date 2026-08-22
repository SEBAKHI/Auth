import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, renderHook, waitFor } from "@testing-library/react"
import type * as React from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

const remoteFetch = vi.hoisted(() => vi.fn())
const deniedFetch = vi.hoisted(() => vi.fn())

vi.mock("./record-sources", () => ({
  MAX_RECORDS_PER_GROUP: 2,
  MIN_RECORD_QUERY: 2,
  RECORD_FETCH_LIMIT: 3,
  RECORD_SOURCES: [
    {
      key: "user",
      headingKey: "nav.users",
      icon: () => null,
      permission: "users:read",
      mode: "remote",
      queryKey: (query: string) => ["test-search", "users", query],
      listRoute: "/users",
      fetch: remoteFetch,
    },
    {
      key: "denied",
      headingKey: "nav.organizations",
      icon: () => null,
      deniedPermission: "organizations:read",
      mode: "local",
      queryKey: () => ["test-search", "denied"],
      listRoute: "/organizations",
      fetch: deniedFetch,
    },
  ],
}))

import { useRecordSearch } from "./use-record-search"

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )
  }
}

const allPermissions = () => true

describe("useRecordSearch", () => {
  beforeEach(() => {
    remoteFetch.mockReset()
    deniedFetch.mockReset()
  })

  it("does not query below the minimum or while the panel is closed", () => {
    const { result, rerender } = renderHook(
      ({ query, enabled }) =>
        useRecordSearch({ query, enabled, hasPermission: allPermissions }),
      {
        initialProps: { query: "a", enabled: true },
        wrapper: wrapper(),
      }
    )

    expect(result.current).toMatchObject({ groups: [], total: 0, isPending: false })
    rerender({ query: "alice", enabled: false })
    expect(remoteFetch).not.toHaveBeenCalled()
  })

  it("filters denied sources, ranks hits, caps rows, and preserves the server total", async () => {
    remoteFetch.mockResolvedValue({
      hits: [
        { id: "user:3", title: "Zed", description: "alice-z", route: "/3" },
        { id: "user:1", title: "Alice", description: "a", route: "/1" },
        { id: "user:2", title: "Alice B", description: "b", route: "/2" },
      ],
      totalCount: 9,
    })
    const { result } = renderHook(
      () =>
        useRecordSearch({
          query: "alice",
          enabled: true,
          hasPermission: allPermissions,
        }),
      { wrapper: wrapper() }
    )

    await waitFor(() => expect(result.current.isPending).toBe(false))
    expect(remoteFetch).toHaveBeenCalledOnce()
    expect(deniedFetch).not.toHaveBeenCalled()
    expect(result.current.groups).toHaveLength(1)
    expect(result.current.groups[0]).toMatchObject({
      sourceKey: "user",
      totalEntries: 9,
    })
    expect(result.current.groups[0].entries.map((entry) => entry.id)).toEqual([
      "user:3",
      "user:1",
    ])
    expect(result.current.total).toBe(2)
  })

  it("reports a failed source and retries only failures", async () => {
    remoteFetch.mockRejectedValueOnce(new Error("offline"))
    const { result } = renderHook(
      () =>
        useRecordSearch({
          query: "alice",
          enabled: true,
          hasPermission: allPermissions,
        }),
      { wrapper: wrapper() }
    )

    await waitFor(() => expect(result.current.isError).toBe(true))
    remoteFetch.mockResolvedValueOnce({ hits: [], totalCount: 0 })
    act(() => result.current.retry())

    await waitFor(() => expect(result.current.isError).toBe(false))
    expect(remoteFetch).toHaveBeenCalledTimes(2)
  })
})
