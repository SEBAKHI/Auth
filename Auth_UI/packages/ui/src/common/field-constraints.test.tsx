import { render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { FieldConstraints } from "./field-constraints"

/**
 * The real locale strings, so the test asserts what an operator reads rather
 * than a key name — the point of this component is the sentence, not the DOM.
 */
const STRINGS: Record<string, string> = {
  "common.range": "Range: {{range}}",
  "common.rangeMin": "Minimum: {{min}}",
  "common.rangeMax": "Maximum: {{max}}",
  "common.defaultValue": "Default: {{value}}",
  "common.enabled": "Enabled",
  "common.disabled": "Disabled",
}

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, vars?: Record<string, unknown>) =>
      Object.entries(vars ?? {}).reduce(
        (text, [name, value]) => text.replace(`{{${name}}}`, String(value)),
        STRINGS[key] ?? key
      ),
  }),
}))

/** Text with the bidi isolates stripped, which is what a reader sees. */
function line() {
  const node = document.querySelector('[data-slot="field-constraints"]')
  return node?.textContent?.replace(/[⁦-⁩]/g, "") ?? null
}

describe("FieldConstraints", () => {
  it("states both bounds and the default together", () => {
    render(<FieldConstraints min={1} max={100000} defaultValue={120} />)
    expect(line()).toBe("Range: 1–100000 · Default: 120")
  })

  it("names a single bound rather than showing half a range", () => {
    render(<FieldConstraints min={0} />)
    expect(line()).toBe("Minimum: 0")
  })

  it("keeps a zero bound, which is a real limit and not an absent one", () => {
    // Grace period allows 0 ("immediate"); a falsy check would drop it and
    // leave the operator thinking the field has no floor.
    render(<FieldConstraints min={0} defaultValue={60} />)
    expect(line()).toBe("Minimum: 0 · Default: 60")
  })

  it("shows a default with no bounds to pair it with", () => {
    render(<FieldConstraints defaultValue="production" />)
    expect(line()).toBe("Default: production")
  })

  it("renders nothing at all when there is nothing to state", () => {
    // An empty line under a field reads as a missing value.
    const { container } = render(<FieldConstraints />)
    expect(container).toBeEmptyDOMElement()
  })

  it("renders nothing for an empty array or empty string default", () => {
    const { container: emptyArray } = render(<FieldConstraints defaultValue={[]} />)
    expect(emptyArray).toBeEmptyDOMElement()

    const { container: emptyText } = render(<FieldConstraints defaultValue="" />)
    expect(emptyText).toBeEmptyDOMElement()
  })

  it("words a boolean default instead of printing true/false", () => {
    render(<FieldConstraints defaultValue={false} />)
    // `false` is a real default and must survive the falsy check.
    expect(line()).toBe("Default: Disabled")
  })

  it("accepts the string bounds the generated API types widen int64 to", () => {
    render(<FieldConstraints min="1" max="3600" />)
    expect(line()).toBe("Range: 1–3600")
  })

  it("isolates the numbers so bidi cannot reverse a range", () => {
    // Without this an Arabic label turns "1–100000" into "100000–1".
    render(<FieldConstraints min={1} max={100000} />)
    const raw = screen.getByText(/Range/).textContent ?? ""
    expect(raw).toContain("⁦1–100000⁩")
  })
})
