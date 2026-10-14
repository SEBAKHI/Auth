import { beforeEach, describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

import {
  getReturnToClientId,
  getReturnToHandle,
  getValidReturnTo,
  toAccountsSearch,
  validateReturnToUrl,
  withoutOrganizationRequest,
} from "./return-to"

const AUTHORIZE = "https://api.example.com/api/v1/auth/authorize"

function search(returnTo: string): string {
  return `?returnTo=${encodeURIComponent(returnTo)}`
}

beforeEach(() => {
  vi.spyOn(console, "warn").mockImplementation(() => {})
})

describe("validateReturnToUrl", () => {
  it("accepts the authorize endpoint on the API origin", () => {
    expect(validateReturnToUrl(`${AUTHORIZE}?client_id=app`)).toBe(
      `${AUTHORIZE}?client_id=app`
    )
  })

  it("accepts any API version", () => {
    const versioned = "https://api.example.com/api/v7/auth/authorize?client_id=app"
    expect(validateReturnToUrl(versioned)).toBe(versioned)
  })

  it.each([
    ["a foreign origin", "https://evil.example.com/api/v1/auth/authorize"],
    ["a lookalike origin", "https://api.example.com.evil.test/api/v1/auth/authorize"],
    ["a different port", "https://api.example.com:8443/api/v1/auth/authorize"],
    ["another endpoint on the API", "https://api.example.com/api/v1/auth/login"],
    ["a path below authorize", "https://api.example.com/api/v1/auth/authorize/evil"],
    ["a protocol-relative URL", "//evil.example.com/api/v1/auth/authorize"],
    ["a javascript: URL", "javascript:alert(1)"],
    ["a data: URL", "data:text/html,<script>alert(1)</script>"],
    ["a relative path", "/api/v1/auth/authorize"],
    ["nonsense", "not a url at all"],
  ])("rejects %s", (_label, raw) => {
    expect(validateReturnToUrl(raw)).toBeNull()
  })

  it.each([null, undefined, ""])("rejects the empty value %s", (raw) => {
    expect(validateReturnToUrl(raw)).toBeNull()
  })

  it("carries the step-up parameters home instead of deleting them", () => {
    // The inverse of what this once asserted, and the point of the change.
    // Stripping prompt=login was how the loop was broken, which meant the demand
    // for a fresh authentication was met by removing the demand — so a stale
    // session sailed through and anyone who deleted the parameter by hand got a
    // code. The server now keeps its own signed record of having demanded
    // step-up and checks the session was minted after it, so the client has
    // nothing to remove and re-authentication is proved rather than assumed.
    const result = validateReturnToUrl(
      `${AUTHORIZE}?client_id=app&prompt=login&max_age=0&state=xyz`
    )

    expect(result).not.toBeNull()
    const params = new URL(result!).searchParams
    expect(params.get("prompt")).toBe("login")
    expect(params.get("max_age")).toBe("0")
    expect(params.get("state")).toBe("xyz")
  })

  it("preserves every parameter the relying party depends on", () => {
    // Deleting a search param re-serializes the whole query string, so this
    // pins that the values a relying party will match on survive the trip.
    const result = validateReturnToUrl(
      `${AUTHORIZE}?response_type=code&client_id=app` +
        `&redirect_uri=${encodeURIComponent("https://rp.example.com/cb?tenant=1")}` +
        `&code_challenge=abc-_123&code_challenge_method=S256` +
        `&state=${encodeURIComponent("a b/c~d")}&scope=openid+profile`
    )

    const params = new URL(result!).searchParams
    expect(params.get("response_type")).toBe("code")
    expect(params.get("client_id")).toBe("app")
    expect(params.get("redirect_uri")).toBe("https://rp.example.com/cb?tenant=1")
    expect(params.get("code_challenge")).toBe("abc-_123")
    expect(params.get("code_challenge_method")).toBe("S256")
    expect(params.get("state")).toBe("a b/c~d")
  })

  it("is idempotent, so re-validating a threaded value is safe", () => {
    const once = validateReturnToUrl(`${AUTHORIZE}?client_id=app&prompt=login`)
    expect(validateReturnToUrl(once)).toBe(once)
  })
})

describe("getValidReturnTo", () => {
  it("reads the returnTo parameter of the login page", () => {
    expect(getValidReturnTo(search(`${AUTHORIZE}?client_id=app`))).toBe(
      `${AUTHORIZE}?client_id=app`
    )
  })

  it("returns null when there is no pending request", () => {
    expect(getValidReturnTo("?foo=bar")).toBeNull()
    expect(getValidReturnTo("")).toBeNull()
  })

  it("returns null for a rejected destination", () => {
    expect(getValidReturnTo(search("https://evil.example.com/"))).toBeNull()
  })

  it("says out loud in dev why a returnTo was dropped", () => {
    // A silent rejection is indistinguishable from no pending request at all,
    // which is exactly what the resume bug looked like from the outside.
    getValidReturnTo(search("https://evil.example.com/"))
    expect(console.warn).toHaveBeenCalledWith(
      expect.stringContaining("returnTo rejected")
    )
  })
})

describe("getReturnToClientId", () => {
  it("extracts the client id for branding lookup", () => {
    expect(getReturnToClientId(`${AUTHORIZE}?client_id=my-app`)).toBe("my-app")
  })

  it("returns null without a pending request", () => {
    expect(getReturnToClientId(null)).toBeNull()
  })
})

// OI-145: the accounts pages carry `?client_id=X&auth_request=H`, and the app
// resumes at the API's resume endpoint. A web application firewall refuses the
// old `?returnTo=<authorize URL>`, a parameter holding an off-site address.
const HANDLE = "0123456789abcdef".repeat(4)
const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`
const HANDLE_SEARCH = `?client_id=app&auth_request=${HANDLE}`

describe("the handle shape (OI-145)", () => {
  it("reads client_id and auth_request as the resume URL on the API origin", () => {
    expect(getValidReturnTo(HANDLE_SEARCH)).toBe(RESUME)
  })

  it("prefers the handle when both shapes arrive", () => {
    expect(
      getValidReturnTo(
        `${HANDLE_SEARCH}&returnTo=${encodeURIComponent(`${AUTHORIZE}?client_id=app`)}`
      )
    ).toBe(RESUME)
  })

  it("still reads the legacy returnTo, for links in flight and the switch turned off", () => {
    expect(getValidReturnTo(search(`${AUTHORIZE}?client_id=app`))).toBe(
      `${AUTHORIZE}?client_id=app`
    )
  })

  it.each([
    ["uppercase hex", "0123456789ABCDEF".repeat(4)],
    ["63 characters", "a".repeat(63)],
    ["65 characters", "a".repeat(65)],
    ["a character outside hex", "g".repeat(64)],
    [
      "an address",
      encodeURIComponent(`https://evil.example.com/${"a".repeat(40)}`),
    ],
    ["a trailing newline", encodeURIComponent(`${"a".repeat(64)}\n`)],
    ["nothing", ""],
  ])("refuses a malformed handle: %s", (_label, handle) => {
    expect(getValidReturnTo(`?client_id=app&auth_request=${handle}`)).toBeNull()
  })

  it("refuses a handle without a client id", () => {
    expect(getValidReturnTo(`?auth_request=${HANDLE}`)).toBeNull()
    expect(getValidReturnTo(`?client_id=&auth_request=${HANDLE}`)).toBeNull()
  })

  it("says out loud in dev why a handle was dropped", () => {
    getValidReturnTo("?client_id=app&auth_request=nope")
    expect(console.warn).toHaveBeenCalledWith(
      expect.stringContaining("auth_request rejected")
    )
  })

  it("validates a resume URL from router state or the 2FA slot, idempotently", () => {
    expect(validateReturnToUrl(RESUME)).toBe(RESUME)
    expect(validateReturnToUrl(validateReturnToUrl(RESUME))).toBe(RESUME)
    expect(validateReturnToUrl(`${RESUME}&skip_organization=true`)).toBe(
      `${RESUME}&skip_organization=true`
    )
    expect(
      validateReturnToUrl(
        `https://api.example.com/api/v3/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`
      )
    ).not.toBeNull()
  })

  it.each([
    [
      "a foreign origin",
      `https://evil.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`,
    ],
    [
      "a path below resume",
      `https://api.example.com/api/v1/auth/authorize/resume/x?client_id=app&auth_request=${HANDLE}`,
    ],
    [
      "a malformed handle",
      "https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=abc",
    ],
    [
      "no client id",
      `https://api.example.com/api/v1/auth/authorize/resume?auth_request=${HANDLE}`,
    ],
    [
      "an extra parameter",
      `${RESUME}&redirect_uri=${encodeURIComponent("https://evil.example.com/cb")}`,
    ],
    ["a repeated parameter", `${RESUME}&client_id=other`],
    ["skip_organization with another value", `${RESUME}&skip_organization=1`],
  ])("refuses a resume URL with %s", (_label, raw) => {
    expect(validateReturnToUrl(raw)).toBeNull()
  })

  it("names the client for branding in both shapes", () => {
    expect(getReturnToClientId(RESUME)).toBe("app")
    expect(getReturnToClientId(`${AUTHORIZE}?client_id=app`)).toBe("app")
  })

  it("gives the organization step the handle of a stored request (OI-150)", () => {
    expect(getReturnToHandle(RESUME)).toBe(HANDLE)
    expect(getReturnToHandle(`${RESUME}&skip_organization=true`)).toBe(HANDLE)
  })

  it.each([
    ["a legacy authorize URL, which names no stored request", `${AUTHORIZE}?client_id=app`],
    ["a resume URL on another origin", RESUME.replace("api.example.com", "evil.example.com")],
    ["a malformed handle", RESUME.replace(HANDLE, HANDLE.toUpperCase())],
    ["nothing", null],
  ])("gives no handle for %s", (_label, raw) => {
    expect(getReturnToHandle(raw)).toBeNull()
  })
})

describe("toAccountsSearch", () => {
  it("writes the handle shape from a resume URL, and nothing else of it", () => {
    expect(toAccountsSearch(RESUME)).toBe(HANDLE_SEARCH)
    expect(toAccountsSearch(`${RESUME}&skip_organization=true`)).toBe(
      HANDLE_SEARCH
    )
    expect(toAccountsSearch(RESUME)).not.toContain("://")
  })

  it("round-trips: the accounts page reads back what was written", () => {
    expect(getValidReturnTo(toAccountsSearch(RESUME))).toBe(RESUME)
    const legacy = `${AUTHORIZE}?client_id=app&state=a%20b`
    expect(getValidReturnTo(toAccountsSearch(legacy))).toBe(legacy)
  })

  it("writes the legacy shape from an authorize URL, exactly as the pages did", () => {
    const legacy = `${AUTHORIZE}?client_id=app&state=xyz`
    expect(toAccountsSearch(legacy)).toBe(
      `?returnTo=${encodeURIComponent(legacy)}`
    )
  })

  it("writes nothing for no request, or for one the allow-list refuses", () => {
    expect(toAccountsSearch(null)).toBe("")
    expect(toAccountsSearch(undefined)).toBe("")
    expect(
      toAccountsSearch("https://evil.example.com/api/v1/auth/authorize")
    ).toBe("")
  })
})

describe("withoutOrganizationRequest", () => {
  it("asks the resume endpoint to skip the organization, keeping the handle", () => {
    const result = new URL(withoutOrganizationRequest(RESUME))
    expect(result.pathname).toBe("/api/v1/auth/authorize/resume")
    expect(result.searchParams.get("skip_organization")).toBe("true")
    expect(result.searchParams.get("auth_request")).toBe(HANDLE)
    expect(result.searchParams.get("client_id")).toBe("app")
    expect(validateReturnToUrl(result.toString())).not.toBeNull()
  })

  it("deletes create_organization from a legacy authorize URL", () => {
    const result = new URL(
      withoutOrganizationRequest(
        `${AUTHORIZE}?client_id=app&create_organization=true&state=s`
      )
    )
    expect(result.searchParams.has("create_organization")).toBe(false)
    expect(result.searchParams.get("state")).toBe("s")
  })
})

/**
 * The shape of the pending request is decided in one module. A seventh link
 * that writes `?returnTo=` by hand would put the whole authorize URL back in
 * front of the firewall for the one flow nobody re-tested, so the source is the
 * test: outside `return-to.ts`, no non-test file writes or reads the parameter.
 */
const sources = import.meta.glob(
  [
    "../../../apps/*/src/**/*.{ts,tsx}",
    "../../*/src/**/*.{ts,tsx}",
    "!../../../**/*.test.{ts,tsx}",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

// The six writers OI-145 routed through toAccountsSearch.
const WRITERS = [
  "packages/auth/src/pages/login.tsx",
  "packages/auth/src/pages/two-factor-verify.tsx",
  "packages/auth/src/pages/verify-email-page.tsx",
  "apps/accounts/src/pages/auth/two-factor.tsx",
  "apps/accounts/src/pages/create-organization.tsx",
  "apps/accounts/src/pages/create-organization-flow.ts",
]

const HERE = import.meta.url
const WORKSPACE_ROOT = HERE.slice(
  0,
  HERE.lastIndexOf("/packages/auth/src/") + 1
)
const files = Object.entries(sources).map(([key, source]) => ({
  path: new URL(key, HERE).href.slice(WORKSPACE_ROOT.length),
  source,
}))

// A query parameter written by hand, or the parameter named in a string (as
// URLSearchParams would take it). Backticks are left out: comments name it so.
const LEGACY_LITERAL = /[?&]returnTo=|(["'])returnTo\1/

describe("one module owns the shape of the pending request", () => {
  it("scans the sources at all", () => {
    // A glob that stopped matching would leave the check below green.
    expect(files.length).toBeGreaterThan(100)
    expect(files.map((file) => file.path)).toEqual(
      expect.arrayContaining(WRITERS)
    )
    expect(files.map((file) => file.path)).toContain(
      "packages/auth/src/return-to.ts"
    )
  })

  it("finds no returnTo literal outside return-to.ts", () => {
    const offenders = files
      .filter((file) => file.path !== "packages/auth/src/return-to.ts")
      .filter((file) => LEGACY_LITERAL.test(file.source))
      .map((file) => file.path)

    expect(offenders).toEqual([])
  })

  it.each(WRITERS)("%s writes through toAccountsSearch", (path) => {
    expect(files.find((file) => file.path === path)?.source).toMatch(
      /\btoAccountsSearch\(/
    )
  })
})
