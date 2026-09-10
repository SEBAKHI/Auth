import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { OTP_CODE_LENGTH, OtpInput } from "./otp-input"

describe("OtpInput", () => {
  it("is what a phone or a password manager recognises as a one-time code", () => {
    // Without these two attributes the code that iOS or Android just received
    // by SMS or mail is not offered above the keyboard, and the keyboard is the
    // full one rather than the digit pad. Neither failure shows up as an error.
    render(<OtpInput value="" onChange={() => undefined} label="Code" />)

    const field = screen.getByRole("textbox", { name: "Code" })
    expect(field).toHaveAttribute("autocomplete", "one-time-code")
    expect(field).toHaveAttribute("inputmode", "numeric")
    expect(field).toHaveAttribute("maxlength", String(OTP_CODE_LENGTH))
  })
})
