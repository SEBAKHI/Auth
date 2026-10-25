import { readFileSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { OneTimeCodeField } from "./one-time-code-field"

/**
 * OI-243 T8: six boxes for every six-digit code, a plain field for a recovery code,
 * and no screen left asking for six digits in a free-text box.
 */
describe("OneTimeCodeField", () => {
  it("is six labelled boxes for a six-digit code, offered as a one-time code", async () => {
    const onChange = vi.fn()
    const { container } = render(
      <OneTimeCodeField id="code" label="Verification code" value="" onChange={onChange} />
    )

    expect(container.querySelectorAll('[data-slot="input-otp-slot"]')).toHaveLength(6)
    const input = screen.getByLabelText("Verification code")
    expect(input).toHaveAttribute("id", "code")
    expect(input).toHaveAttribute("autocomplete", "one-time-code")
    expect(input).toHaveAttribute("inputmode", "numeric")

    // Digits only: the letter never reaches the form.
    await userEvent.setup().type(input, "a1")
    expect(onChange.mock.calls).toEqual([["1"]])
  })

  it("keeps the browser hint off where the screen says so", () => {
    render(<OneTimeCodeField id="code" label="Code" value="" onChange={vi.fn()} autoComplete="off" />)

    expect(screen.getByLabelText("Code")).toHaveAttribute("autocomplete", "off")
  })

  it("is one plain, left-to-right field for a recovery code", () => {
    const { container } = render(
      <OneTimeCodeField id="code" label="Recovery code" value="" onChange={vi.fn()} recovery />
    )

    expect(container.querySelectorAll('[data-slot="input-otp-slot"]')).toHaveLength(0)
    const input = screen.getByLabelText("Recovery code")
    expect(input).toHaveAttribute("dir", "ltr")
    expect(input).toHaveAttribute("autocomplete", "off")
    expect(input).not.toHaveAttribute("inputmode")
  })
})

/**
 * Every screen that asks for a second-step code outside the sign-in page goes
 * through the shared field: a count per file, so a field reverted to a free-text
 * box — or a new one added beside it — fails here, not in front of the owner.
 */
describe("the screens that ask for a second-step code", () => {
  const sites: Record<string, number> = {
    "../../../account/src/pages/profile/email-code-card.tsx": 2,
    "../../../account/src/pages/profile/profile-security.tsx": 1,
    "../../../account/src/pages/profile/two-factor-maintenance.tsx": 2,
    "../../../auth/src/two-factor-enrollment.tsx": 3,
    "../../../auth/src/pages/two-factor-required.tsx": 1,
  }

  it.each(Object.entries(sites))("%s uses the shared field %i times and no numeric text box", (path, count) => {
    const source = readFileSync(fileURLToPath(new URL(path, import.meta.url)), "utf8")

    expect(source.match(/<OneTimeCodeField\b/g)?.length ?? 0).toBe(count)
    expect(source).not.toMatch(/inputMode=/)
  })
})
