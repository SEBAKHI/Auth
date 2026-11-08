import ts from "typescript"
import { describe, expect, it } from "vitest"

/**
 * S20 T-C1: one door to `@simplewebauthn/browser`. Only the loader names the
 * package, and only by dynamic import (plus type-only exports, which the build
 * erases): a static import anywhere would put the helper into that chunk, and
 * into every first chunk that reaches it (login-payload.spec.ts). The profile's
 * passkey card goes through the loader too, never the package.
 */

const sources = import.meta.glob(
  [
    "../../../../apps/*/src/**/*.{ts,tsx}",
    "../../../*/src/**/*.{ts,tsx}",
    "!../../../../**/*.test.{ts,tsx}",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

const HERE = import.meta.url
const WORKSPACE_ROOT = HERE.slice(0, HERE.lastIndexOf("/packages/auth/src/") + 1)
const files = Object.entries(sources).map(([key, source]) => ({
  path: new URL(key, HERE).href.slice(WORKSPACE_ROOT.length),
  source,
}))

const PACKAGE = "@simplewebauthn/browser"
const LOADER = "packages/auth/src/passkey/webauthn.ts"

/**
 * Every place the package is named as a module, with how, read by the compiler's
 * own parser: a pattern over the text let a static import hide inside the type
 * line above it (the deliberate break that found it).
 */
function mentions(source: string) {
  const found: string[] = []
  const names = (node: ts.Node | undefined) =>
    node !== undefined && ts.isStringLiteral(node) && node.text === PACKAGE
  const visit = (node: ts.Node) => {
    if (ts.isImportDeclaration(node) && names(node.moduleSpecifier)) {
      found.push(node.importClause?.isTypeOnly ? "import type" : "import")
    } else if (ts.isExportDeclaration(node) && names(node.moduleSpecifier)) {
      found.push(node.isTypeOnly ? "export type" : "export")
    } else if (
      ts.isCallExpression(node) &&
      node.expression.kind === ts.SyntaxKind.ImportKeyword &&
      names(node.arguments[0])
    ) {
      found.push("dynamic")
    } else if (ts.isImportTypeNode(node)) {
      const literal = ts.isLiteralTypeNode(node.argument) ? node.argument.literal : undefined
      if (names(literal)) found.push("typeof import")
    }
    ts.forEachChild(node, visit)
  }
  visit(ts.createSourceFile("webauthn.ts", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS))
  return found
}

describe("one door to the WebAuthn helper (T-C1)", () => {
  it("scans the sources at all", () => {
    expect(files.length).toBeGreaterThan(100)
    expect(files.map((file) => file.path)).toContain(LOADER)
    expect(files.map((file) => file.path)).toContain(
      "packages/account/src/pages/profile/passkeys-card.tsx"
    )
  })

  it("names the package in the loader alone", () => {
    const naming = files.filter((file) => file.source.includes(PACKAGE)).map((file) => file.path)
    expect(naming).toEqual([LOADER])
  })

  it("loads it by dynamic import only; the rest is types the build erases", () => {
    const loader = files.find((file) => file.path === LOADER)!
    const kinds = mentions(loader.source)
    expect(kinds.filter((kind) => kind === "dynamic")).toHaveLength(1)
    expect(
      kinds.filter((kind) => !["dynamic", "import type", "export type", "typeof import"].includes(kind))
    ).toEqual([])
  })

  it("tells a static import from the dynamic one", () => {
    expect(mentions(`import * as helper from "${PACKAGE}"`)).toEqual(["import"])
    expect(mentions(`export { startRegistration } from "${PACKAGE}"`)).toEqual(["export"])
    expect(mentions(`import type { X } from "${PACKAGE}"`)).toEqual(["import type"])
    expect(mentions(`type H = typeof import("${PACKAGE}")\nvoid import("${PACKAGE}")`)).toEqual([
      "typeof import",
      "dynamic",
    ])
  })

  it("is reached through the loader from the profile, never by the package", () => {
    const card = files.find((file) => file.path.endsWith("/profile/passkeys-card.tsx"))!
    expect(card.source).toContain("@authsystem/auth/passkey/webauthn")
    expect(card.source).not.toContain(PACKAGE)
  })
})
