import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import * as React from "react"
import { describe, expect, it } from "vitest"

import { Dialog, DialogContent, DialogTitle } from "./dialog"
import { NativeSelect } from "./native-select"

function DialogNativeSelectFixture() {
  const [value, setValue] = React.useState("")

  return (
    <Dialog open>
      <DialogContent>
        <DialogTitle>Transfer ownership</DialogTitle>
        <NativeSelect
          aria-label="Choose member"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        >
          <option value="" disabled>
            Choose member
          </option>
          <option value="member-1">First member</option>
        </NativeSelect>
      </DialogContent>
    </Dialog>
  )
}

describe("NativeSelect inside Dialog", () => {
  it("selects an option without creating a portal or dismissing the dialog", async () => {
    const user = userEvent.setup()
    render(<DialogNativeSelectFixture />)

    const select = screen.getByRole("combobox", { name: "Choose member" })
    expect(select).toBeInstanceOf(HTMLSelectElement)
    expect(document.querySelector("[data-radix-popper-content-wrapper]")).toBeNull()

    await user.selectOptions(select, "member-1")

    expect(select).toHaveValue("member-1")
    expect(screen.getByRole("dialog")).toBeVisible()
  })
})
