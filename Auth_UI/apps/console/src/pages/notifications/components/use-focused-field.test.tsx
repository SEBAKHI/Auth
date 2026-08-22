import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import { useFocusedField } from "./use-focused-field"

function Harness() {
  const { onFocusCapture, insert } = useFocusedField()
  return (
    <div onFocusCapture={onFocusCapture}>
      <input aria-label="input" defaultValue="alpha" />
      <textarea aria-label="textarea" defaultValue="bravo" />
      <input aria-label="disabled" defaultValue="locked" disabled />
      <output aria-label="result" />
      <button
        type="button"
        onClick={() => {
          const output = screen.getByLabelText("result")
          output.textContent = String(insert("{{token}}"))
        }}
      >
        insert
      </button>
    </div>
  )
}

describe("useFocusedField", () => {
  it("refuses insertion until an editable field has focus", async () => {
    render(<Harness />)

    await userEvent.click(screen.getByRole("button", { name: "insert" }))

    expect(screen.getByLabelText("result")).toHaveTextContent("false")
  })

  it("replaces the selected input range through the native React setter", async () => {
    render(<Harness />)
    const input = screen.getByLabelText("input") as HTMLInputElement
    input.focus()
    input.setSelectionRange(1, 4)

    await userEvent.click(screen.getByRole("button", { name: "insert" }))

    expect(input).toHaveValue("a{{token}}a")
    expect(input.selectionStart).toBe(10)
    expect(screen.getByLabelText("result")).toHaveTextContent("true")
  })

  it("uses the textarea setter and refuses disabled fields", async () => {
    render(<Harness />)
    const textarea = screen.getByLabelText("textarea") as HTMLTextAreaElement
    textarea.focus()
    textarea.setSelectionRange(5, 5)
    await userEvent.click(screen.getByRole("button", { name: "insert" }))
    expect(textarea).toHaveValue("bravo{{token}}")

    const disabled = screen.getByLabelText("disabled") as HTMLInputElement
    disabled.dispatchEvent(new FocusEvent("focusin", { bubbles: true }))
    await userEvent.click(screen.getByRole("button", { name: "insert" }))
    expect(screen.getByLabelText("result")).toHaveTextContent("false")
  })
})
