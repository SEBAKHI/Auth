import { readdirSync, readFileSync } from "node:fs"
import { join } from "node:path"

import { beforeEach, describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

import { CLIENT_ID_PAGES, themeScopeFor } from "./theme-scope"

/**
 * The first frame of an accounts page is painted with one scope's cached
 * appearance: the application the page names, or the platform's. A page of a
 * pending request names the request's application, validated as every page
 * validates it; the sign-out pages and the expired request name the one in
 * their `client_id` (owner, 2026-10-29: every page that names an application
 * takes its appearance). An address that names an application any other way is
 * the platform's.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const at = (pathname: string, search: string) => ({ pathname, search })

beforeEach(() => {
  vi.spyOn(console, "warn").mockImplementation(() => {})
})

describe("themeScopeFor", () => {
  it("is the application of a stored request", () => {
    expect(themeScopeFor(at("/login", `?client_id=crm&auth_request=${HANDLE}`))).toBe("crm")
  })

  it("is the application of a legacy authorize request on the API's origin", () => {
    const returnTo = "https://api.example.com/api/v1/auth/authorize?client_id=crm&response_type=code"
    expect(themeScopeFor(at("/login", `?returnTo=${encodeURIComponent(returnTo)}`))).toBe("crm")
  })

  it.each(["/logout", "/signed-out", "/sign-in-expired", "/Signed-Out", "/signed-out/"])(
    "is the application %s names by its client id",
    (pathname) => {
      expect(themeScopeFor(at(pathname, "?client_id=crm&state=xyz"))).toBe("crm")
    }
  )

  it.each([
    ["no request at all", "/login", ""],
    ["a client id alone on a page of the flow", "/login", "?client_id=crm"],
    ["a client id alone on any other page", "/profile", "?client_id=crm"],
    ["a page that names one by client id, without one", "/signed-out", ""],
    ["a page that names one by client id, with an empty one", "/logout", "?client_id="],
    ["a path that only starts like such a page", "/signed-out-elsewhere", "?client_id=crm"],
    ["a malformed handle", "/login", "?client_id=crm&auth_request=not-a-handle"],
    ["a request on another origin", "/login", `?returnTo=${encodeURIComponent("https://evil.example/api/v1/auth/authorize?client_id=crm")}`],
    ["another path on the API's origin", "/login", `?returnTo=${encodeURIComponent("https://api.example.com/elsewhere?client_id=crm")}`],
  ])("is the platform's for %s", (_name, pathname, search) => {
    expect(themeScopeFor(at(pathname, search))).toBeNull()
  })
})

/**
 * The list of pages is one place; the pages and the routes are two others. The
 * first frame names the same application as the page only while the three agree,
 * and nothing fails when they drift: a page that started naming its application
 * by client id would simply paint the platform's colours first. So this reads
 * the sources.
 */
describe("CLIENT_ID_PAGES", () => {
  const pagesDir = join(__dirname, "pages")
  const routes = readFileSync(join(__dirname, "routes.tsx"), "utf8")

  /** The page modules that name their application by client id. */
  const byClientId = readdirSync(pagesDir)
    .filter((file) => /\.tsx$/.test(file) && !/\.test\.tsx$/.test(file))
    .filter((file) =>
      /useFlowBranding\(\s*\{\s*clientId\b/.test(readFileSync(join(pagesDir, file), "utf8"))
    )
    .map((file) => file.replace(/\.tsx$/, ""))
    .sort()

  /** The path each route that lazily loads one of those modules is served at. */
  const routed = [...routes.matchAll(/path:\s*"([^"]+)",\s*lazy:\s*lazyRoute\(\s*\(\)\s*=>\s*import\("\.\/pages\/([^"]+)"\)/g)]
    .filter(([, , module]) => byClientId.includes(module))
    .map(([, path]) => path)
    .sort()

  it("finds the pages that name their application by client id", () => {
    // A floor, so a pattern that matches nothing cannot pass the case below.
    expect(byClientId).toEqual(["logout", "sign-in-expired", "signed-out"])
  })

  it("is exactly the routes of those pages", () => {
    expect(routed).toEqual([...CLIENT_ID_PAGES].sort())
  })
})
