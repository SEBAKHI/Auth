import { render, screen } from "@testing-library/react"
import {
  href,
  MemoryRouter,
  Route,
  Routes,
  useParams,
} from "react-router-dom"
import { describe, expect, it } from "vitest"

import { routePath } from "./route-path"

describe("routePath", () => {
  it("leaves an ordinary identifier untouched", () => {
    const id = "0f8fad5b-d9cb-469f-a165-70867728950e"
    expect(routePath`/users/${id}`).toBe(`/users/${id}`)
  })

  it("keeps a value that carries a slash inside one segment", () => {
    expect(routePath`/users/${"a/b"}`).toBe("/users/a%2Fb")
  })

  it("stops a value from opening a query string or a fragment", () => {
    expect(routePath`/users/${"x?admin=1"}`).toBe("/users/x%3Fadmin%3D1")
    expect(routePath`/users/${"x#top"}`).toBe("/users/x%23top")
  })

  it("encodes the characters that would otherwise re-target the path", () => {
    expect(routePath`/users/${"../../applications/1"}`).toBe(
      "/users/..%2F..%2Fapplications%2F1"
    )
    expect(routePath`/users/${"100%"}`).toBe("/users/100%25")
  })

  it("carries non-ASCII values", () => {
    expect(routePath`/notifications/policy/${"نسخة"}`).toBe(
      "/notifications/policy/%D9%86%D8%B3%D8%AE%D8%A9"
    )
  })

  it("interpolates every value, not only the first", () => {
    expect(routePath`/a/${"1 2"}/b/${"3&4"}`).toBe("/a/1%202/b/3%264")
  })

  it("returns a literal path when there is nothing to interpolate", () => {
    expect(routePath`/notifications/templates`).toBe(
      "/notifications/templates"
    )
  })
})

describe("why this exists rather than react-router's href()", () => {
  // Pinned because the obvious "simplification" is to delete this module and
  // call href() instead. href() substitutes the raw value into the pattern, so
  // the same id routePath keeps inside one segment escapes it there.
  it("href() does not encode, and the value escapes its segment", () => {
    expect(href("/users/:id", { id: "a/b" })).toBe("/users/a/b")
    expect(routePath`/users/${"a/b"}`).toBe("/users/a%2Fb")
  })
})

describe("round trip through the router", () => {
  function ShowId() {
    const { id } = useParams()
    return <span data-testid="id">{id}</span>
  }

  // The encoding is only correct if the value the route hands back is the value
  // that went in - including the characters that would have broken the match.
  it.each([
    ["0f8fad5b-d9cb-469f-a165-70867728950e", "a plain id"],
    ["a/b", "an id containing a slash"],
    ["x?admin=1", "an id that looks like a query string"],
    ["x#top", "an id that looks like a fragment"],
    ["100%", "an id containing a percent sign"],
    ["نسخة", "a non-ASCII id"],
  ])("%s survives as %s", (id) => {
    render(
      <MemoryRouter initialEntries={[routePath`/users/${id}`]}>
        <Routes>
          <Route path="/users/:id" element={<ShowId />} />
        </Routes>
      </MemoryRouter>
    )

    expect(screen.getByTestId("id")).toHaveTextContent(id)
  })
})
