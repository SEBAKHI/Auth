import * as React from "react"
import { useQueries } from "@tanstack/react-query"
import type { LucideIcon } from "lucide-react"

import { rankRecords, type RecordEntry } from "./build-index"
import {
  MAX_RECORDS_PER_GROUP,
  MIN_RECORD_QUERY,
  RECORD_FETCH_LIMIT,
  RECORD_SOURCES,
} from "./record-sources"

/** One source's answer to the current query, ready to render as a group. */
export interface RecordGroup {
  sourceKey: string
  headingKey: string
  icon: LucideIcon
  /** Where the rest of the matches are, for a group the cap truncated. */
  listRoute: string
  entries: RecordEntry[]
  /** Everything that matched, so a capped group can say what it left out. */
  totalEntries: number
}

export interface RecordSearchState {
  groups: RecordGroup[]
  /** Rows shown, for the screen-reader count. */
  total: number
  /** A search is in flight; the record half of the panel has nothing yet. */
  isPending: boolean
  /** At least one source failed. The others may still have answered. */
  isError: boolean
  retry: () => void
}

const IDLE: RecordSearchState = {
  groups: [],
  total: 0,
  isPending: false,
  isError: false,
  retry: () => {},
}

/**
 * Searches the platform's records — users, roles, applications, organizations,
 * notification templates and layouts — one request per source per query.
 *
 * The fan-out is bounded by permission, not by chance: a source the viewer may
 * not read is never queried, so the panel neither leaks the existence of records
 * nor spends a request learning it would have been a 403. The API remains the
 * authoritative check.
 *
 * `query` must already be debounced. Nothing fires below
 * {@link MIN_RECORD_QUERY} characters — one letter matches most of the platform,
 * and every source would issue a request per keystroke to say so. Pages and
 * settings are unaffected: they are in memory and answer from the first
 * character.
 */
export function useRecordSearch({
  query,
  enabled,
  hasPermission,
}: {
  query: string
  enabled: boolean
  hasPermission: (permission: string | undefined) => boolean
}): RecordSearchState {
  const sources = React.useMemo(
    () =>
      RECORD_SOURCES.filter(
        (source) =>
          hasPermission(source.permission) &&
          !(source.deniedPermission && hasPermission(source.deniedPermission))
      ),
    [hasPermission]
  )

  const term = query.trim()
  const active = enabled && term.length >= MIN_RECORD_QUERY

  const results = useQueries({
    queries: sources.map((source) => ({
      // A local source's key omits the term: its payload is the whole (small)
      // list, fetched once and filtered in memory for every later keystroke.
      queryKey: source.queryKey(source.mode === "remote" ? term : ""),
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        source.fetch({ query: term, signal, limit: RECORD_FETCH_LIMIT }),
      enabled: active,
      staleTime: source.mode === "local" ? 5 * 60_000 : 30_000,
      // One attempt. The shared client retries once so a transient 401 can
      // survive a token refresh, but a palette that waits out a second round
      // trip before admitting a source is down has already lost the user.
      retry: false,
      // Deliberately no `keepPreviousData`: holding the last query's rows under
      // a changed input means Enter can open a record that does not match what
      // is on screen. Skeletons hold the geometry instead.
    })),
  })

  const isPending = active && results.some((result) => result.isPending)
  const isError = results.some((result) => result.isError)

  const groups = React.useMemo(() => {
    if (!active) return []

    return sources.flatMap<RecordGroup>((source, index) => {
      const page = results[index]?.data
      if (!page) return []

      const entries = rankRecords(
        page.hits.map<RecordEntry>((hit) => ({
          ...hit,
          kind: "record",
          sourceKey: source.key,
          // Nothing invisible to match on: a record's searchable text is its
          // name and the line under it, both of which are on screen.
          keywords: "",
        })),
        term,
        source.mode
      )
      if (entries.length === 0) return []

      return [
        {
          sourceKey: source.key,
          headingKey: source.headingKey,
          icon: source.icon,
          listRoute: source.listRoute,
          entries: entries.slice(0, MAX_RECORDS_PER_GROUP),
          // The server's count where there is one; otherwise what the in-memory
          // filter matched, which for a whole-list source is the true total.
          totalEntries: page.totalCount ?? entries.length,
        },
      ]
    })
    // `results` is a fresh array every render, so this recomputes on each one.
    // It is a sort of at most a few dozen rows, and the alternative — keying on
    // a hand-rolled signature of the results — is a cache invalidation bug
    // waiting to happen.
  }, [active, sources, results, term])

  const retry = React.useCallback(() => {
    for (const result of results) {
      if (result.isError) void result.refetch()
    }
  }, [results])

  if (!active) return IDLE

  return {
    groups,
    total: groups.reduce((sum, group) => sum + group.entries.length, 0),
    isPending,
    isError,
    retry,
  }
}
