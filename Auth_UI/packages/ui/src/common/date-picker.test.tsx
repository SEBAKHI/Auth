import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import * as React from "react"
import { describe, expect, it } from "vitest"

import { DatePicker, monthsFromNow } from "./date-picker"

function Fixture({
  initial = "",
  minDate,
  maxDate,
}: {
  initial?: string
  minDate?: Date
  maxDate?: Date
}) {
  const [value, setValue] = React.useState(initial)
  return (
    <>
      <DatePicker
        value={value}
        onChange={(next) => setValue(next ?? "")}
        minDate={minDate}
        maxDate={maxDate}
      />
      <output data-testid="value">{value}</output>
    </>
  )
}

async function openPicker(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button"))
}

describe("DatePicker", () => {
  it("offers month AND year dropdowns, not just a month label", async () => {
    const user = userEvent.setup()
    render(<Fixture initial="2026-05-14" />)

    await openPicker(user)

    // The whole point of captionLayout="dropdown": jumping years must not
    // require clicking the chevron twelve times per year.
    expect(screen.getByRole("combobox", { name: /month/i })).toBeInTheDocument()
    expect(screen.getByRole("combobox", { name: /year/i })).toBeInTheDocument()
  })

  it("bounds the year dropdown to minDate..maxDate", async () => {
    const user = userEvent.setup()
    const min = new Date(2024, 0, 1)
    const max = new Date(2027, 11, 31)
    render(<Fixture initial="2026-05-14" minDate={min} maxDate={max} />)

    await openPicker(user)

    const years = screen
      .getByRole("combobox", { name: /year/i })
      .querySelectorAll("option")
    const labels = [...years].map((option) => option.textContent)

    expect(labels).toContain("2024")
    expect(labels).toContain("2027")
    expect(labels).not.toContain("2023")
    expect(labels).not.toContain("2028")
  })

  it("emits yyyy-MM-dd so it drops into a form field unchanged", async () => {
    const user = userEvent.setup()
    render(<Fixture initial="2026-05-14" />)

    await openPicker(user)
    await user.click(screen.getByRole("button", { name: /May 20th, 2026/ }))

    expect(screen.getByTestId("value")).toHaveTextContent("2026-05-20")
  })

  it("disables days outside the allowed range", async () => {
    const user = userEvent.setup()
    // Whole month selectable except before the 10th.
    render(
      <Fixture
        initial="2026-05-14"
        minDate={new Date(2026, 4, 10)}
        maxDate={monthsFromNow(10)}
      />
    )

    await openPicker(user)

    expect(screen.getByRole("button", { name: /May 9th, 2026/ })).toBeDisabled()
    expect(screen.getByRole("button", { name: /May 11th, 2026/ })).toBeEnabled()
  })

  it("clears back to an empty value", async () => {
    const user = userEvent.setup()
    render(<Fixture initial="2026-05-14" />)

    await openPicker(user)
    await user.click(screen.getByRole("button", { name: /clear/i }))

    expect(screen.getByTestId("value")).toBeEmptyDOMElement()
  })
})
