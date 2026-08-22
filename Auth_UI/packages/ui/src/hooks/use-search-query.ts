import * as React from "react"
import type { SortingState } from "@tanstack/react-table"
import { useSearchParams } from "react-router-dom"

/** The stable search parameter shared by list routes and Global Search. */
export const SEARCH_QUERY_PARAM = "q"

export const LIST_PAGE_SIZES = [10, 20, 50, 100] as const

const DEFAULT_MAX_PAGE = 100_000
const DEFAULT_MAX_SEARCH_LENGTH = 200
const NO_SORT_VALUE = "none"

export interface ListUrlFilter<T> {
  /** Override the property name when the public URL needs a different label. */
  param?: string
  defaultValue: T
  parse: (raw: string | null) => T
  serialize: (value: T) => string | null
}

type FilterSchema<TFilters extends Record<string, unknown>> = {
  [Key in keyof TFilters]: ListUrlFilter<TFilters[Key]>
}

export interface ListUrlStateOptions<
  TFilters extends Record<string, unknown> = Record<string, never>,
> {
  /** Prefixes embedded tables (`users.page`, `users.sort`, ...). */
  namespace?: string
  defaultPageSize: number
  pageSizes?: readonly number[]
  maxPage?: number
  maxSearchLength?: number
  defaultSorting?: SortingState
  sortableColumns: readonly string[]
  filters?: FilterSchema<TFilters>
}

export interface ListUrlState<TFilters extends Record<string, unknown>> {
  search: string
  pageIndex: number
  pageSize: number
  sorting: SortingState
  filters: TFilters
}

export interface ListUrlStateController<
  TFilters extends Record<string, unknown>,
> extends ListUrlState<TFilters> {
  setSearch: (value: string) => void
  setPageIndex: (value: number) => void
  setPageSize: (value: number) => void
  setSorting: (value: SortingState) => void
  setFilter: <Key extends keyof TFilters>(
    key: Key,
    value: TFilters[Key]
  ) => void
  setFilters: (values: Partial<TFilters>) => void
}

function isValidDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const [year, month, day] = value.split("-").map(Number)
  const date = new Date(Date.UTC(year, month - 1, day))
  return (
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month - 1 &&
    date.getUTCDate() === day
  )
}

/** A bounded, optionally pattern-constrained string query parameter. */
export function stringUrlFilter(options?: {
  param?: string
  maxLength?: number
  pattern?: RegExp
}): ListUrlFilter<string> {
  const maxLength = options?.maxLength ?? DEFAULT_MAX_SEARCH_LENGTH
  const sanitize = (value: string | null): string => {
    if (!value) return ""
    const bounded = value.slice(0, maxLength)
    return !options?.pattern || options.pattern.test(bounded) ? bounded : ""
  }
  return {
    param: options?.param,
    defaultValue: "",
    parse: sanitize,
    serialize: (value) => sanitize(value) || null,
  }
}

/** A compact boolean parameter: present true values canonicalize to `1`. */
export function booleanUrlFilter(param?: string): ListUrlFilter<boolean> {
  return {
    param,
    defaultValue: false,
    parse: (raw) => raw === "1" || raw === "true",
    serialize: (value) => (value ? "1" : null),
  }
}

/** An ISO calendar date (`YYYY-MM-DD`), rejected when the date is impossible. */
export function dateUrlFilter(param?: string): ListUrlFilter<string> {
  return {
    param,
    defaultValue: "",
    parse: (raw) => (raw && isValidDate(raw) ? raw : ""),
    serialize: (value) => (isValidDate(value) ? value : null),
  }
}

/** A single allow-listed value. */
export function enumUrlFilter<const TValue extends string>(
  values: readonly TValue[],
  param?: string
): ListUrlFilter<TValue | ""> {
  const allowed = new Set<string>(values)
  const sanitize = (value: string | null): TValue | "" =>
    value && allowed.has(value) ? (value as TValue) : ""
  return {
    param,
    defaultValue: "",
    parse: sanitize,
    serialize: (value) => sanitize(value) || null,
  }
}

/** A comma-separated, de-duplicated allow-list used by faceted table filters. */
export function enumArrayUrlFilter<const TValue extends string>(
  values: readonly TValue[],
  param?: string
): ListUrlFilter<TValue[]> {
  const allowed = new Set<string>(values)
  const sanitize = (input: string | readonly string[] | null): TValue[] => {
    const valuesToCheck = Array.isArray(input)
      ? input
      : typeof input === "string"
        ? input.split(",")
        : []
    return Array.from(
      new Set(
        valuesToCheck.filter((value): value is TValue => allowed.has(value))
      )
    )
  }
  return {
    param,
    defaultValue: [],
    parse: sanitize,
    serialize: (value) => sanitize(value).join(",") || null,
  }
}

/** Bounded free-form values for facets whose options come from loaded data. */
export function stringArrayUrlFilter(options?: {
  param?: string
  maxItems?: number
  maxValueLength?: number
  pattern?: RegExp
}): ListUrlFilter<string[]> {
  const maxItems = options?.maxItems ?? 20
  const maxValueLength = options?.maxValueLength ?? 100
  const maxSerializedLength = maxItems * (maxValueLength * 6 + 3) + 2
  const sanitize = (input: string | readonly string[] | null): string[] => {
    let values: readonly string[] = []
    if (Array.isArray(input)) values = input
    else if (typeof input === "string" && input.length <= maxSerializedLength) {
      if (input.startsWith("[")) {
        try {
          const parsed: unknown = JSON.parse(input)
          values = Array.isArray(parsed)
            ? parsed.filter(
                (value): value is string => typeof value === "string"
              )
            : []
        } catch {
          values = []
        }
      } else {
        // Accept links emitted before the collision-safe JSON representation.
        values = input.split(",")
      }
    }
    return Array.from(
      new Set(
        values
          .map((value) => value.slice(0, maxValueLength))
          .filter(
            (value) =>
              Boolean(value) &&
              (!options?.pattern || options.pattern.test(value))
          )
      )
    ).slice(0, maxItems)
  }
  return {
    param: options?.param,
    defaultValue: [],
    parse: sanitize,
    serialize: (value) => {
      const safe = sanitize(value)
      return safe.length ? JSON.stringify(safe) : null
    },
  }
}

function paramName(namespace: string | undefined, name: string): string {
  return namespace ? `${namespace}.${name}` : name
}

function positiveInteger(
  raw: string | null,
  fallback: number,
  maximum: number
): number {
  if (!raw || !/^\d+$/.test(raw)) return fallback
  const value = Number(raw)
  return Number.isSafeInteger(value) && value >= 1 && value <= maximum
    ? value
    : fallback
}

function sanitizedDefaultSorting(
  options: ListUrlStateOptions<Record<string, unknown>>
): SortingState {
  const first = options.defaultSorting?.[0]
  return first && options.sortableColumns.includes(first.id)
    ? [{ id: first.id, desc: Boolean(first.desc) }]
    : []
}

function readSorting(
  params: URLSearchParams,
  options: ListUrlStateOptions<Record<string, unknown>>
): SortingState {
  const defaultSorting = sanitizedDefaultSorting(options)
  const sort = params.get(paramName(options.namespace, "sort"))
  if (!sort) return defaultSorting
  if (sort === NO_SORT_VALUE) return []
  if (!options.sortableColumns.includes(sort)) return defaultSorting

  const direction = params.get(paramName(options.namespace, "direction"))
  if (direction && direction !== "asc" && direction !== "desc") {
    return defaultSorting
  }
  return [{ id: sort, desc: direction === "desc" }]
}

function readFilters<TFilters extends Record<string, unknown>>(
  params: URLSearchParams,
  options: ListUrlStateOptions<TFilters>
): TFilters {
  const filters = {} as TFilters
  for (const key of Object.keys(options.filters ?? {}) as Array<
    keyof TFilters
  >) {
    const codec = options.filters?.[key]
    if (!codec) continue
    const name = paramName(options.namespace, codec.param ?? String(key))
    filters[key] = codec.parse(params.get(name))
  }
  return filters
}

/** Parse and bound every URL-owned value before it reaches a query key or API. */
export function readListUrlState<TFilters extends Record<string, unknown>>(
  params: URLSearchParams,
  options: ListUrlStateOptions<TFilters>
): ListUrlState<TFilters> {
  const pageSizes = options.pageSizes ?? LIST_PAGE_SIZES
  const defaultPageSize = pageSizes.includes(options.defaultPageSize)
    ? options.defaultPageSize
    : LIST_PAGE_SIZES[1]
  const requestedPageSize = positiveInteger(
    params.get(paramName(options.namespace, "pageSize")),
    defaultPageSize,
    Math.max(...pageSizes)
  )
  const pageSize = pageSizes.includes(requestedPageSize)
    ? requestedPageSize
    : defaultPageSize
  const search = (
    params.get(paramName(options.namespace, SEARCH_QUERY_PARAM)) ?? ""
  ).slice(0, options.maxSearchLength ?? DEFAULT_MAX_SEARCH_LENGTH)

  return {
    search,
    pageIndex:
      positiveInteger(
        params.get(paramName(options.namespace, "page")),
        1,
        options.maxPage ?? DEFAULT_MAX_PAGE
      ) - 1,
    pageSize,
    sorting: readSorting(
      params,
      options as ListUrlStateOptions<Record<string, unknown>>
    ),
    filters: readFilters(params, options),
  }
}

function sortingMatches(left: SortingState, right: SortingState): boolean {
  const a = left[0]
  const b = right[0]
  return (
    (!a && !b) || (a?.id === b?.id && Boolean(a?.desc) === Boolean(b?.desc))
  )
}

/** Write one canonical representation while preserving parameters owned elsewhere. */
export function writeListUrlState<TFilters extends Record<string, unknown>>(
  source: URLSearchParams,
  state: ListUrlState<TFilters>,
  options: ListUrlStateOptions<TFilters>
): URLSearchParams {
  const result = new URLSearchParams(source)
  const key = (name: string) => paramName(options.namespace, name)
  const owned = [
    key(SEARCH_QUERY_PARAM),
    key("page"),
    key("pageSize"),
    key("sort"),
    key("direction"),
  ]
  for (const filterKey of Object.keys(options.filters ?? {}) as Array<
    keyof TFilters
  >) {
    const codec = options.filters?.[filterKey]
    if (codec) owned.push(key(codec.param ?? String(filterKey)))
  }
  owned.forEach((name) => result.delete(name))

  const boundedSearch = state.search.slice(
    0,
    options.maxSearchLength ?? DEFAULT_MAX_SEARCH_LENGTH
  )
  if (boundedSearch) result.set(key(SEARCH_QUERY_PARAM), boundedSearch)

  const maxPage = options.maxPage ?? DEFAULT_MAX_PAGE
  const pageIndex = Number.isSafeInteger(state.pageIndex)
    ? Math.min(Math.max(state.pageIndex, 0), maxPage - 1)
    : 0
  if (pageIndex > 0) result.set(key("page"), String(pageIndex + 1))

  const pageSizes = options.pageSizes ?? LIST_PAGE_SIZES
  const pageSize = pageSizes.includes(state.pageSize)
    ? state.pageSize
    : options.defaultPageSize
  if (pageSize !== options.defaultPageSize) {
    result.set(key("pageSize"), String(pageSize))
  }

  const defaultSorting = sanitizedDefaultSorting(
    options as ListUrlStateOptions<Record<string, unknown>>
  )
  const requestedSort = state.sorting[0]
  const sorting =
    requestedSort && options.sortableColumns.includes(requestedSort.id)
      ? [{ id: requestedSort.id, desc: Boolean(requestedSort.desc) }]
      : []
  if (!sortingMatches(sorting, defaultSorting)) {
    const first = sorting[0]
    if (!first) result.set(key("sort"), NO_SORT_VALUE)
    else {
      result.set(key("sort"), first.id)
      result.set(key("direction"), first.desc ? "desc" : "asc")
    }
  }

  for (const filterKey of Object.keys(options.filters ?? {}) as Array<
    keyof TFilters
  >) {
    const codec = options.filters?.[filterKey]
    if (!codec) continue
    const serialized = codec.serialize(
      state.filters[filterKey] ?? codec.defaultValue
    )
    if (serialized)
      result.set(key(codec.param ?? String(filterKey)), serialized)
  }

  return result
}

/**
 * URL-owned state for a server-paginated list.
 *
 * Text search replaces the current history entry, while discrete pagination,
 * sorting and filter changes create navigable entries. Every update is atomic:
 * page reset and the changed value are committed in one URL transition.
 */
export function useListUrlState<TFilters extends Record<string, unknown>>(
  options: ListUrlStateOptions<TFilters>
): ListUrlStateController<TFilters> {
  const [params, setParams] = useSearchParams()
  const state = readListUrlState(params, options)
  const canonical = writeListUrlState(params, state, options)
  const currentSearch = params.toString()
  const canonicalSearch = canonical.toString()

  React.useEffect(() => {
    if (currentSearch === canonicalSearch) return
    setParams(new URLSearchParams(canonicalSearch), { replace: true })
  }, [canonicalSearch, currentSearch, setParams])

  const commit = React.useCallback(
    (
      update: (current: ListUrlState<TFilters>) => ListUrlState<TFilters>,
      replace: boolean
    ) => {
      setParams(
        (currentParams) => {
          const current = readListUrlState(currentParams, options)
          return writeListUrlState(currentParams, update(current), options)
        },
        { replace }
      )
    },
    [options, setParams]
  )

  const setSearch = React.useCallback(
    (value: string) =>
      commit((current) => ({ ...current, search: value, pageIndex: 0 }), true),
    [commit]
  )
  const setPageIndex = React.useCallback(
    (value: number) =>
      commit((current) => ({ ...current, pageIndex: value }), false),
    [commit]
  )
  const setPageSize = React.useCallback(
    (value: number) =>
      commit(
        (current) => ({ ...current, pageIndex: 0, pageSize: value }),
        false
      ),
    [commit]
  )
  const setSorting = React.useCallback(
    (value: SortingState) =>
      commit(
        (current) => ({ ...current, pageIndex: 0, sorting: value.slice(0, 1) }),
        false
      ),
    [commit]
  )
  const setFilter = React.useCallback(
    <Key extends keyof TFilters>(key: Key, value: TFilters[Key]) =>
      commit(
        (current) => ({
          ...current,
          pageIndex: 0,
          filters: { ...current.filters, [key]: value },
        }),
        false
      ),
    [commit]
  )
  const setFilters = React.useCallback(
    (values: Partial<TFilters>) =>
      commit(
        (current) => ({
          ...current,
          pageIndex: 0,
          filters: { ...current.filters, ...values },
        }),
        false
      ),
    [commit]
  )

  return {
    ...state,
    setSearch,
    setPageIndex,
    setPageSize,
    setSorting,
    setFilter,
    setFilters,
  }
}
