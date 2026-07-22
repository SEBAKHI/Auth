import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { InputOTP, InputOTPGroup, InputOTPSlot } from "./input-otp"

function OtpFixture({ dir = "ltr" }: { dir?: "ltr" | "rtl" }) {
  return (
    <InputOTP dir={dir} maxLength={2} value="" onChange={() => undefined}>
      <InputOTPGroup data-testid="otp-group">
        <InputOTPSlot index={0} />
        <InputOTPSlot index={1} />
      </InputOTPGroup>
    </InputOTP>
  )
}

describe("InputOTP direction", () => {
  it("keeps the hidden input, visual container, and slots LTR together", () => {
    const { container } = render(<OtpFixture />)

    expect(screen.getByRole("textbox")).toHaveAttribute("dir", "ltr")
    expect(screen.getByTestId("otp-group")).toHaveAttribute("dir", "ltr")
    expect(container.querySelector("[data-input-otp-container]")).toHaveClass(
      "[direction:ltr]"
    )
  })

  it("keeps an explicit RTL direction consistent across every layer", () => {
    const { container } = render(<OtpFixture dir="rtl" />)

    expect(screen.getByRole("textbox")).toHaveAttribute("dir", "rtl")
    expect(screen.getByTestId("otp-group")).toHaveAttribute("dir", "rtl")
    expect(container.querySelector("[data-input-otp-container]")).toHaveClass(
      "[direction:rtl]"
    )
  })
})
