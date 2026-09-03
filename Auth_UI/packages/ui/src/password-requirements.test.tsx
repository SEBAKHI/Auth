import { render, screen, within } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { PasswordRuleState } from "@authsystem/api/password-policy"

import { PasswordRequirements } from "./password-requirements"

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    // Echo the key and any interpolation, so a test can see both what was
    // asked for and what was passed.
    t: (key: string, options?: Record<string, unknown>) =>
      options && Object.keys(options).some((k) => options[k] !== undefined)
        ? `${key}:${JSON.stringify(options)}`
        : key,
  }),
}))

const RULES: PasswordRuleState[] = [
  { id: "minLength", met: false, count: 8 },
  { id: "uppercase", met: true },
  { id: "digit", met: false },
]

describe("PasswordRequirements", () => {
  it("renders one labelled line per rule, with the minimum interpolated", () => {
    render(<PasswordRequirements rules={RULES} />)

    const list = screen.getByRole("list", { name: "auth.passwordRulesTitle" })
    const items = within(list).getAllByRole("listitem")

    expect(items).toHaveLength(3)
    expect(items[0]).toHaveTextContent('auth.passwordRuleMinLength:{"count":8}')
    expect(items[1]).toHaveTextContent("auth.passwordRuleUppercase")
    expect(items[2]).toHaveTextContent("auth.passwordRuleDigit")
  })

  it("exposes each rule's state as data and as hidden text, never by colour alone", () => {
    render(<PasswordRequirements rules={RULES} />)

    const items = screen.getAllByRole("listitem")

    expect(items[0]).toHaveAttribute("data-met", "false")
    expect(items[0]).toHaveTextContent("auth.passwordRuleUnmet")
    expect(items[1]).toHaveAttribute("data-met", "true")
    expect(items[1]).toHaveTextContent("auth.passwordRuleMet")
    expect(items[1].querySelector("svg")).not.toBeNull()
  })

  it("announces progress through a single polite live region", () => {
    const { container } = render(<PasswordRequirements rules={RULES} />)

    const live = container.querySelectorAll('[aria-live="polite"]')

    expect(live).toHaveLength(1)
    expect(live[0]).toHaveTextContent(
      'auth.passwordRulesProgress:{"met":1,"total":3}'
    )
  })

  it("tells the person the server checks more than the list shows", () => {
    render(<PasswordRequirements rules={RULES} />)

    expect(
      screen.getByText("auth.passwordRulesAlsoChecked")
    ).toBeInTheDocument()
  })
})
