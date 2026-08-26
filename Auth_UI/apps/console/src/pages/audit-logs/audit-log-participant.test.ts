import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import {
  AUDIT_PARTICIPANT_ROLES,
  participantRoleParam,
} from "./audit-log-participant"

/**
 * The console sends this filter as a NUMBER, the way it sends `sortDirection` —
 * so the mapping below is a wire contract whose other half is a C# enum, and
 * nothing about it is visible from either side alone.
 *
 * The failure it guards against is the quiet kind: reorder the enum in C# and
 * every request still succeeds, every saved link still resolves, and every one
 * of them starts asking a different question than it was saved to ask. On an
 * audit trail that is not a broken filter, it is a wrong answer with a correct
 * appearance.
 *
 * Reads the C# source the same way `audit-catalog.test.ts` reads the action
 * catalogue and `server-sort-contract.test.ts` reads the sort allow-list.
 */
function enumSource(): string {
  let directory = process.cwd()
  for (let depth = 0; depth < 10; depth += 1) {
    const candidate = join(
      directory,
      "Auth/Auth.Domain/Enums/AuditParticipantRole.cs"
    )
    if (existsSync(candidate)) return readFileSync(candidate, "utf8")
    const parent = dirname(directory)
    if (parent === directory) break
    directory = parent
  }
  throw new Error("AuditParticipantRole.cs not found above " + process.cwd())
}

/** Every `Name = <ordinal>` member, lower-cased to match the console's tokens. */
function csharpOrdinals(): Map<string, number> {
  const found = new Map<string, number>()
  for (const [, name, value] of enumSource().matchAll(
    /^\s{4}(\w+)\s*=\s*(\d+)\s*,?\s*$/gm
  )) {
    found.set(name.toLowerCase(), Number(value))
  }
  return found
}

describe("the participant role the console sends", () => {
  it("finds the enum members", () => {
    // Without this, a parse that silently returns nothing makes every case
    // below pass by matching an empty set against an empty set.
    expect(csharpOrdinals().size).toBe(3)
  })

  it("sends each role as the ordinal the API assigns it", () => {
    const ordinals = csharpOrdinals()
    for (const role of AUDIT_PARTICIPANT_ROLES) {
      expect(ordinals.has(role), `C# has no ${role} member`).toBe(true)
      expect(
        participantRoleParam(role),
        `${role} maps to the wrong ordinal`
      ).toBe(ordinals.get(role))
    }
  })

  it("offers every role the API accepts", () => {
    // The reverse direction: a role added in C# and not here is a capability
    // the screen silently does not offer.
    expect([...AUDIT_PARTICIPANT_ROLES].sort()).toEqual(
      [...csharpOrdinals().keys()].sort()
    )
  })

  it("opens on the widest reading, and names the narrower ones apart", () => {
    // Order is display order: "both" first, because that is what a reader means
    // by a person's audit log.
    expect(AUDIT_PARTICIPANT_ROLES[0]).toBe("either")
    expect(new Set(AUDIT_PARTICIPANT_ROLES).size).toBe(
      AUDIT_PARTICIPANT_ROLES.length
    )
  })
})
