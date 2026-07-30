import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import * as React from "react"
import { describe, expect, it } from "vitest"

// Side-effect init, so the Custom chip resolves its real `common.custom` string
// rather than echoing the key back.
import "@astoom/i18n"

import { PresetField, type Preset } from "./preset-field"

const PRESETS: Preset[] = [
  { value: "", label: "Off" },
  { value: "15", label: "15 min" },
  { value: "60", label: "1 h" },
]

function Fixture({ initial = "60" }: { initial?: string }) {
  const [value, setValue] = React.useState(initial)
  return (
    <>
      <PresetField presets={PRESETS} value={value} onChange={setValue}>
        {({ value: current, onChange }) => (
          <input
            aria-label="Custom value"
            value={current}
            onChange={(event) => onChange(event.target.value)}
          />
        )}
      </PresetField>
      <output data-testid="value">{value}</output>
    </>
  )
}

describe("PresetField", () => {
  it("marks the preset matching the current value, not a stored choice", () => {
    render(<Fixture initial="15" />)

    expect(screen.getByRole("radio", { name: "15 min" })).toBeChecked()
    expect(screen.getByRole("radio", { name: "1 h" })).not.toBeChecked()
    // Custom is not active, so its control stays hidden.
    expect(screen.queryByLabelText("Custom value")).toBeNull()
  })

  it("writes the preset's value when a chip is picked", async () => {
    const user = userEvent.setup()
    render(<Fixture />)

    await user.click(screen.getByRole("radio", { name: "15 min" }))

    expect(screen.getByTestId("value")).toHaveTextContent("15")
  })

  it("treats an empty-string preset as a real choice", async () => {
    const user = userEvent.setup()
    render(<Fixture />)

    await user.click(screen.getByRole("radio", { name: "Off" }))

    expect(screen.getByTestId("value")).toBeEmptyDOMElement()
    expect(screen.getByRole("radio", { name: "Off" })).toBeChecked()
  })

  it("opens on Custom when the value matches no preset, keeping that value", () => {
    render(<Fixture initial="45" />)

    expect(screen.getByRole("radio", { name: "Custom" })).toBeChecked()
    expect(screen.getByLabelText("Custom value")).toHaveValue("45")
  })

  it("keeps Custom selected while the typed value still matches a preset", async () => {
    const user = userEvent.setup()
    render(<Fixture initial="60" />)

    await user.click(screen.getByRole("radio", { name: "Custom" }))
    // The value is still 60, which IS a preset — the chip must not snap back,
    // or the custom control would vanish from under the user.
    expect(screen.getByRole("radio", { name: "Custom" })).toBeChecked()
    expect(screen.getByLabelText("Custom value")).toBeInTheDocument()

    await user.type(screen.getByLabelText("Custom value"), "5")

    expect(screen.getByTestId("value")).toHaveTextContent("605")
  })

  it("never lets the field lose its value by deselecting the active chip", async () => {
    const user = userEvent.setup()
    render(<Fixture initial="15" />)

    await user.click(screen.getByRole("radio", { name: "15 min" }))

    expect(screen.getByTestId("value")).toHaveTextContent("15")
    expect(screen.getByRole("radio", { name: "15 min" })).toBeChecked()
  })

  it("renders no Custom chip when no custom control is supplied", () => {
    render(
      <PresetField presets={PRESETS} value="60" onChange={() => {}} />
    )

    expect(screen.queryByRole("radio", { name: "Custom" })).toBeNull()
  })
})
