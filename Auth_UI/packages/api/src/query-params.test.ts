// This guard reads source files, so it needs Node's types. The app tsconfigs
// deliberately keep Node globals out of browser code, so they are pulled in
// here for this file alone rather than widened project-wide.
/// <reference types="node" />
import fs from "node:fs"
import path from "node:path"
import ts from "typescript"
import { describe, expect, it } from "vitest"

/**
 * Guards every `params.query` key against the generated OpenAPI schema.
 *
 * TypeScript does not do this for us. `openapi-fetch` passes the options
 * object through generics that defeat excess-property checking, so
 * `params.query` accepts any key at all — a call site sending a key the
 * endpoint has never heard of compiles clean on a forced build. Measured, not
 * assumed: a deliberately nonsensical `zzzTotallyBogusParam` produced no error.
 *
 * The server ignores an unrecognized query parameter and returns the
 * unfiltered first page, so the mistake surfaces as "search is broken" or, far
 * worse, as a page that quietly shows everything. No 400, no console error,
 * nothing to grep for. That is exactly how a misspelled `search` survived
 * review in the trial-user picker until a human noticed the list was not
 * narrowing.
 *
 * This test is the missing check: it parses the schema for the keys each
 * endpoint actually accepts, then walks every `api.GET/POST/PUT/DELETE/PATCH`
 * call in the workspace and compares.
 */

const METHODS = new Set(["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE"])

/** Vitest runs from the workspace root, where vitest.config.ts lives. */
function workspaceRoot(): string {
  const root = process.cwd()
  if (!fs.existsSync(path.join(root, "vitest.config.ts"))) {
    throw new Error(
      `expected to run from the Auth_UI workspace root, but ${root} has no vitest.config.ts — ` +
        "this guard resolves source paths relative to the working directory"
    )
  }
  return root
}

function parse(file: string): ts.SourceFile {
  return ts.createSourceFile(
    file,
    fs.readFileSync(file, "utf8"),
    ts.ScriptTarget.Latest,
    /* setParentNodes */ true,
    file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS
  )
}

function memberName(member: ts.TypeElement | ts.ObjectLiteralElementLike): string | undefined {
  const name = member.name
  if (!name) return undefined
  if (ts.isIdentifier(name) || ts.isStringLiteral(name)) return name.text
  return undefined
}

/**
 * Reads the generated schema into "METHOD path" -> the query keys that
 * endpoint accepts. An endpoint whose query is `never` maps to an empty set,
 * which is itself meaningful: it accepts no query keys at all.
 */
function allowedQueryKeys(schemaFile: string): Map<string, Set<string>> {
  const allowed = new Map<string, Set<string>>()
  const source = parse(schemaFile)

  const pathsInterface = source.statements.find(
    (statement): statement is ts.InterfaceDeclaration =>
      ts.isInterfaceDeclaration(statement) && statement.name.text === "paths"
  )

  if (!pathsInterface) {
    throw new Error(`no 'paths' interface in ${schemaFile} — has the generator changed shape?`)
  }

  for (const route of pathsInterface.members) {
    const routePath = memberName(route)
    if (!routePath || !ts.isPropertySignature(route) || !route.type) continue
    if (!ts.isTypeLiteralNode(route.type)) continue

    for (const operation of route.type.members) {
      const method = memberName(operation)?.toUpperCase()
      if (!method || !METHODS.has(method)) continue
      if (!ts.isPropertySignature(operation) || !operation.type) continue
      if (!ts.isTypeLiteralNode(operation.type)) continue

      const parameters = operation.type.members.find((m) => memberName(m) === "parameters")
      if (!parameters || !ts.isPropertySignature(parameters) || !parameters.type) continue
      if (!ts.isTypeLiteralNode(parameters.type)) continue

      const query = parameters.type.members.find((m) => memberName(m) === "query")
      const keys = new Set<string>()

      if (query && ts.isPropertySignature(query) && query.type && ts.isTypeLiteralNode(query.type)) {
        for (const key of query.type.members) {
          const name = memberName(key)
          if (name) keys.add(name)
        }
      }

      allowed.set(`${method} ${routePath}`, keys)
    }
  }

  return allowed
}

interface QueryKey {
  name: string
  /** Line of the key itself, so the failure points at the word to change. */
  line: number
}

interface CallSite {
  file: string
  line: number
  method: string
  route: string
  keys: QueryKey[]
  /** Keys this test could not read statically (spreads, computed names). */
  opaque: number
}

/** Every `api.<METHOD>("<path>", { params: { query: { ... } } })` in one file. */
function callSites(file: string, root: string): CallSite[] {
  const source = parse(file)
  const found: CallSite[] = []

  const visit = (node: ts.Node): void => {
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)) {
      const method = node.expression.name.text
      const receiver = node.expression.expression

      const isApiClient = ts.isIdentifier(receiver) && receiver.text === "api"

      if (isApiClient && METHODS.has(method)) {
        const [routeArg, optionsArg] = node.arguments

        if (routeArg && ts.isStringLiteral(routeArg) && optionsArg && ts.isObjectLiteralExpression(optionsArg)) {
          const params = optionsArg.properties.find(
            (p): p is ts.PropertyAssignment =>
              ts.isPropertyAssignment(p) && memberName(p) === "params"
          )

          if (params && ts.isObjectLiteralExpression(params.initializer)) {
            const query = params.initializer.properties.find(
              (p): p is ts.PropertyAssignment =>
                ts.isPropertyAssignment(p) && memberName(p) === "query"
            )

            if (query && ts.isObjectLiteralExpression(query.initializer)) {
              const keys: QueryKey[] = []
              let opaque = 0

              for (const property of query.initializer.properties) {
                if (ts.isSpreadAssignment(property)) {
                  opaque++
                  continue
                }
                const name = memberName(property)
                if (name) {
                  const at = source.getLineAndCharacterOfPosition(property.getStart(source))
                  keys.push({ name, line: at.line + 1 })
                } else {
                  opaque++
                }
              }

              const { line } = source.getLineAndCharacterOfPosition(node.getStart(source))
              found.push({
                file: path.relative(root, file).replace(/\\/g, "/"),
                line: line + 1,
                method,
                route: routeArg.text,
                keys,
                opaque,
              })
            }
          }
        }
      }
    }

    ts.forEachChild(node, visit)
  }

  visit(source)
  return found
}

/**
 * The accepted key a mistyped one most likely meant: same word in different
 * casing, or one being a prefix of the other. `search` vs `searchTerm` — the
 * mistake this guard exists for — is the prefix case.
 */
function nearest(key: string, accepted: Set<string>): string | undefined {
  const lower = key.toLowerCase()

  return (
    [...accepted].find((candidate) => candidate.toLowerCase() === lower) ??
    [...accepted].find((candidate) => {
      const other = candidate.toLowerCase()
      return other.startsWith(lower) || lower.startsWith(other)
    })
  )
}

function sourceFiles(root: string): string[] {
  const roots = [path.join(root, "apps"), path.join(root, "packages")]
  const files: string[] = []

  const walk = (dir: string): void => {
    if (!fs.existsSync(dir)) return
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name)
      if (entry.isDirectory()) {
        if (entry.name === "node_modules" || entry.name === "dist") continue
        walk(full)
      } else if (/\.tsx?$/.test(entry.name) && !entry.name.endsWith(".d.ts")) {
        files.push(full)
      }
    }
  }

  roots.forEach(walk)
  return files
}

describe("api query parameters", () => {
  const root = workspaceRoot()
  const allowed = allowedQueryKeys(path.join(root, "packages", "api", "src", "schema.d.ts"))
  const sites = sourceFiles(root).flatMap((file) => callSites(file, root))

  it("every query key exists on the endpoint being called", () => {
    const unknown: string[] = []

    for (const site of sites) {
      const keys = allowed.get(`${site.method} ${site.route}`)

      if (!keys) {
        unknown.push(
          `${site.file}:${site.line} calls ${site.method} ${site.route}, which the schema does not define`
        )
        continue
      }

      for (const key of site.keys) {
        if (keys.has(key.name)) continue

        const suggestion = nearest(key.name, keys)
        unknown.push(
          `${site.file}:${key.line} sends '${key.name}' to ${site.method} ${site.route}` +
            (suggestion
              ? ` — did you mean '${suggestion}'?`
              : ` (accepts: ${[...keys].join(", ") || "no query parameters"})`)
        )
      }
    }

    expect(
      unknown,
      "the server silently ignores an unrecognized query parameter and returns the " +
        "unfiltered first page, so these would ship as a list that never narrows " +
        "rather than as an error"
    ).toEqual([])
  })

  it("actually inspected the call sites, so it cannot pass vacuously", () => {
    // 47 call sites pass a query today. A floor well under that survives
    // ordinary churn; a collapse to near zero means the walker stopped
    // recognising the client and this guard is watching nothing.
    expect(sites.length).toBeGreaterThan(30)
    expect(allowed.size).toBeGreaterThan(50)
  })

  it("reads nearly every key statically, so little hides behind a spread", () => {
    // A spread cannot be resolved without type information. Tracking the count
    // keeps the blind spot visible instead of letting it grow unnoticed.
    const opaque = sites.reduce((total, site) => total + site.opaque, 0)
    expect(opaque).toBeLessThanOrEqual(2)
  })
})
