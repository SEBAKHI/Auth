import { describe, expect, it } from "vitest"
import type { TFunction } from "i18next"

import { escapeCsvCell, rowsToCsvString, type ExportColumn } from "./csv"

// formatFieldValue only calls t() for booleans; a passthrough is enough here.
const t = ((key: string) => key) as unknown as TFunction

describe("escapeCsvCell", () => {
  it("leaves a plain value unquoted", () => {
    expect(escapeCsvCell("hello")).toBe("hello")
  })

  it("quotes values containing a comma", () => {
    expect(escapeCsvCell("a,b")).toBe('"a,b"')
  })

  it("quotes values containing a newline", () => {
    expect(escapeCsvCell("a\nb")).toBe('"a\nb"')
  })

  it("doubles embedded quotes", () => {
    expect(escapeCsvCell('a "b"')).toBe('"a ""b"""')
  })

  it("neutralizes spreadsheet formula injection", () => {
    expect(escapeCsvCell("=SUM(A1)")).toBe('"\'=SUM(A1)"')
    expect(escapeCsvCell("+1")).toBe('"\'+1"')
    expect(escapeCsvCell("-1")).toBe('"\'-1"')
    expect(escapeCsvCell("@cmd")).toBe('"\'@cmd"')
  })
})

describe("rowsToCsvString", () => {
  const field = (key: string) => (row: unknown) =>
    (row as Record<string, unknown>)[key]
  const columns: ExportColumn[] = [
    { label: "Name", getValue: field("name") },
    { label: "Age", getValue: field("age") },
  ]

  it("emits a header row followed by one CRLF-delimited line per record", () => {
    const csv = rowsToCsvString(
      [
        { name: "Alice", age: 30 },
        { name: "Bob", age: 40 },
      ],
      columns,
      t
    )
    expect(csv).toBe("Name,Age\r\nAlice,30\r\nBob,40")
  })

  it("renders empty and missing fields as an em dash", () => {
    const csv = rowsToCsvString([{ name: "Alice", age: null }], columns, t)
    expect(csv).toBe("Name,Age\r\nAlice,—")
  })

  it("escapes header labels and cell values", () => {
    const csv = rowsToCsvString(
      [{ note: "a,b" }],
      [{ label: "A, B", getValue: field("note") }],
      t
    )
    expect(csv).toBe('"A, B"\r\n"a,b"')
  })
})
