import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom"
import { describe, expect, it } from "vitest"

import {
  booleanUrlFilter,
  dateUrlFilter,
  enumArrayUrlFilter,
  enumUrlFilter,
  readListUrlState,
  stringArrayUrlFilter,
  stringUrlFilter,
  useListUrlState,
  writeListUrlState,
  type ListUrlStateOptions,
} from "./use-search-query"
import { useTabParam } from "./use-tab-param"

type TestFilters = {
  deleted: boolean
  from: string
  statuses: Array<"Active" | "Locked">
}

const LIST_OPTIONS = {
  defaultPageSize: 20,
  sortableColumns: ["name", "createdAt"],
  defaultSorting: [{ id: "createdAt", desc: true }],
  filters: {
    deleted: booleanUrlFilter(),
    from: dateUrlFilter(),
    statuses: enumArrayUrlFilter(["Active", "Locked"], "status"),
  },
} satisfies ListUrlStateOptions<TestFilters>

function ListProbe() {
  const state = useListUrlState(LIST_OPTIONS)
  const location = useLocation()
  const navigate = useNavigate()
  return (
    <>
      <output aria-label="state">
        {JSON.stringify({
          search: state.search,
          page: state.pageIndex,
          pageSize: state.pageSize,
          sorting: state.sorting,
          filters: state.filters,
        })}
      </output>
      <output aria-label="url">{`${location.pathname}${location.search}`}</output>
      <button type="button" onClick={() => state.setSearch("alice")}>
        search
      </button>
      <button type="button" onClick={() => state.setSearch("alice smith")}>
        refine
      </button>
      <button type="button" onClick={() => state.setPageIndex(2)}>
        page
      </button>
      <button type="button" onClick={() => state.setPageSize(50)}>
        size
      </button>
      <button
        type="button"
        onClick={() => state.setSorting([{ id: "name", desc: false }])}
      >
        sort
      </button>
      <button type="button" onClick={() => state.setFilter("deleted", true)}>
        deleted
      </button>
      <button
        type="button"
        onClick={() => state.setFilters({ from: "2026-08-21", deleted: true })}
      >
        filters
      </button>
      <button type="button" onClick={() => navigate(-1)}>
        back
      </button>
    </>
  )
}

function TabProbe() {
  const [tab, setTab] = useTabParam(["account", "sessions", "security"])
  const location = useLocation()
  return (
    <>
      <output aria-label="tab">{tab}</output>
      <output aria-label="url">{location.search}</output>
      <button type="button" onClick={() => setTab("security")}>
        security
      </button>
      <button type="button" onClick={() => setTab("account")}>
        account
      </button>
    </>
  )
}

describe("list URL state codec", () => {
  it("reads a valid deep link into bounded typed state", () => {
    const state = readListUrlState(
      new URLSearchParams(
        "q=alice&page=3&pageSize=50&sort=name&direction=asc&deleted=true&from=2026-08-21&status=Locked,Active,Locked"
      ),
      LIST_OPTIONS
    )

    expect(state).toEqual({
      search: "alice",
      pageIndex: 2,
      pageSize: 50,
      sorting: [{ id: "name", desc: false }],
      filters: {
        deleted: true,
        from: "2026-08-21",
        statuses: ["Locked", "Active"],
      },
    })
  })

  it("falls back from malicious or impossible values and canonicalizes them", () => {
    const params = new URLSearchParams(
      `keep=1&q=${"x".repeat(250)}&page=-2&pageSize=10000&sort=secret&direction=sideways&from=2026-02-30&status=Unknown`
    )
    const state = readListUrlState(params, LIST_OPTIONS)

    expect(state.search).toHaveLength(200)
    expect(state.pageIndex).toBe(0)
    expect(state.pageSize).toBe(20)
    expect(state.sorting).toEqual([{ id: "createdAt", desc: true }])
    expect(state.filters).toEqual({ deleted: false, from: "", statuses: [] })

    const canonical = writeListUrlState(params, state, LIST_OPTIONS)
    expect(canonical.get("keep")).toBe("1")
    expect(canonical.get("q")).toHaveLength(200)
    expect(canonical.has("page")).toBe(false)
    expect(canonical.has("pageSize")).toBe(false)
    expect(canonical.has("sort")).toBe(false)
    expect(canonical.has("direction")).toBe(false)
    expect(canonical.has("from")).toBe(false)
    expect(canonical.has("status")).toBe(false)
  })

  it("represents an explicit cleared sort and namespaces embedded lists", () => {
    const options = {
      namespace: "members",
      defaultPageSize: 20,
      sortableColumns: ["name"],
      defaultSorting: [{ id: "name", desc: false }],
    } satisfies ListUrlStateOptions
    const written = writeListUrlState(
      new URLSearchParams("tab=members&page=9"),
      {
        search: "omar",
        pageIndex: 1,
        pageSize: 20,
        sorting: [],
        filters: {},
      },
      options
    )

    expect(written.toString()).toBe(
      "tab=members&page=9&members.q=omar&members.page=2&members.sort=none"
    )
    expect(readListUrlState(written, options).sorting).toEqual([])
  })

  it("sanitizes reusable string, enum, date and boolean filters", () => {
    const identifier = stringUrlFilter({
      maxLength: 8,
      pattern: /^[a-z-]+$/,
    })
    const status = enumUrlFilter(["open", "closed"])

    expect(identifier.parse("valid-id-too-long")).toBe("valid-id")
    expect(identifier.serialize("BAD")).toBeNull()
    expect(status.parse("unknown")).toBe("")
    expect(status.serialize("closed")).toBe("closed")
    expect(dateUrlFilter().parse("2024-02-29")).toBe("2024-02-29")
    expect(dateUrlFilter().parse("2025-02-29")).toBe("")
    expect(booleanUrlFilter().parse("1")).toBe(true)
    expect(booleanUrlFilter().parse("0")).toBe(false)
    expect(
      stringArrayUrlFilter({ maxItems: 2, maxValueLength: 4 }).parse(
        "alpha,beta,alpha,gamma"
      )
    ).toEqual(["alph", "beta"])
    const freeForm = stringArrayUrlFilter()
    const serialized = freeForm.serialize(["Sales, EMEA", "Owner"])
    expect(serialized).toBe('["Sales, EMEA","Owner"]')
    expect(freeForm.parse(serialized)).toEqual(["Sales, EMEA", "Owner"])
    expect(freeForm.parse("[broken")).toEqual([])
    expect(freeForm.parse("x".repeat(20_000))).toEqual([])
  })
})

describe("list URL state hook", () => {
  it("restores a deep link and cleans invalid values without a loop", async () => {
    render(
      <MemoryRouter
        initialEntries={[
          "/users?keep=1&page=oops&pageSize=50&sort=name&direction=desc&deleted=1",
        ]}
      >
        <ListProbe />
      </MemoryRouter>
    )

    expect(screen.getByLabelText("state")).toHaveTextContent('"pageSize":50')
    expect(screen.getByLabelText("state")).toHaveTextContent('"desc":true')
    expect(screen.getByLabelText("state")).toHaveTextContent('"deleted":true')
    await waitFor(() =>
      expect(screen.getByLabelText("url")).toHaveTextContent(
        "/users?keep=1&pageSize=50&sort=name&direction=desc&deleted=1"
      )
    )
  })

  it("replaces search keystrokes while preserving unrelated parameters", async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter
        initialEntries={["/before", "/users?tab=all&page=4"]}
        initialIndex={1}
      >
        <ListProbe />
      </MemoryRouter>
    )

    await user.click(screen.getByRole("button", { name: "search" }))
    await user.click(screen.getByRole("button", { name: "refine" }))
    expect(screen.getByLabelText("url")).toHaveTextContent(
      "/users?tab=all&q=alice+smith"
    )
    await user.click(screen.getByRole("button", { name: "back" }))
    expect(screen.getByLabelText("url")).toHaveTextContent("/before")
  })

  it("pushes discrete page, size, sort and filter transitions atomically", async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter initialEntries={["/users?page=4"]}>
        <ListProbe />
      </MemoryRouter>
    )

    await user.click(screen.getByRole("button", { name: "page" }))
    expect(screen.getByLabelText("url")).toHaveTextContent("/users?page=3")
    await user.click(screen.getByRole("button", { name: "size" }))
    expect(screen.getByLabelText("url")).toHaveTextContent("/users?pageSize=50")
    await user.click(screen.getByRole("button", { name: "sort" }))
    expect(screen.getByLabelText("url")).toHaveTextContent(
      "/users?pageSize=50&sort=name&direction=asc"
    )
    await user.click(screen.getByRole("button", { name: "deleted" }))
    expect(screen.getByLabelText("url")).toHaveTextContent(
      "/users?pageSize=50&sort=name&direction=asc&deleted=1"
    )
    await user.click(screen.getByRole("button", { name: "filters" }))
    expect(screen.getByLabelText("url")).toHaveTextContent("from=2026-08-21")
  })
})

describe("tab URL state", () => {
  it("falls back from an invalid tab and writes/removes valid tab state", async () => {
    render(
      <MemoryRouter initialEntries={["/profile?tab=unknown&keep=1"]}>
        <TabProbe />
      </MemoryRouter>
    )

    expect(screen.getByLabelText("tab")).toHaveTextContent("account")
    await userEvent.click(screen.getByRole("button", { name: "security" }))
    expect(screen.getByLabelText("tab")).toHaveTextContent("security")
    expect(screen.getByLabelText("url")).toHaveTextContent(
      "?tab=security&keep=1"
    )
    await userEvent.click(screen.getByRole("button", { name: "account" }))
    expect(screen.getByLabelText("url")).toHaveTextContent("?keep=1")
  })
})
