import { describe, expect, it } from "vitest"

import { permissionMatches } from "./permission-matching"

/**
 * These cases mirror PermissionRequirementHandler.PermissionMatches on the API
 * side. The two must agree: where the UI is stricter a legitimate holder sees
 * an empty console, and where it is looser it advertises actions that 403.
 */
describe("permissionMatches", () => {
  it("lets the global wildcard through", () => {
    expect(permissionMatches(["*"], "users:read")).toBe(true)
    expect(permissionMatches(["*"], "secrets.manage")).toBe(true)
  })

  it("accepts an exact code", () => {
    expect(permissionMatches(["users:read"], "users:read")).toBe(true)
  })

  it("accepts a prefix wildcard over its own area", () => {
    // The case the UI used to get wrong. A holder of users:* passed every API
    // call and was refused by every render gate keyed on a leaf code.
    expect(permissionMatches(["users:*"], "users:read")).toBe(true)
    expect(permissionMatches(["users:*"], "users:manage-roles")).toBe(true)
    expect(permissionMatches(["org:members:*"], "org:members:invite")).toBe(true)
  })

  it("treats the wildcard's own stem as covered", () => {
    expect(permissionMatches(["org:members:*"], "org:members")).toBe(true)
  })

  it("does not let a wildcard reach outside its area", () => {
    expect(permissionMatches(["users:*"], "roles:read")).toBe(false)
    expect(permissionMatches(["users:*"], "users-admin:read")).toBe(false)
  })

  it("matches by string prefix, not by hierarchy", () => {
    // The seed scripts' auth:-prefixed codes look like ancestors of the short
    // codes the controllers enforce. They are not, on either side.
    expect(permissionMatches(["auth:users:*"], "users:read")).toBe(false)
    expect(permissionMatches(["auth:*"], "users:read")).toBe(false)
  })

  it("ignores case, as the API does", () => {
    expect(permissionMatches(["Users:*"], "users:read")).toBe(true)
  })

  it("refuses when nothing is held", () => {
    expect(permissionMatches([], "users:read")).toBe(false)
  })

  it("does not treat a dot-separated code as covered by a colon wildcard", () => {
    expect(permissionMatches(["secrets:*"], "secrets.manage")).toBe(false)
  })
})
