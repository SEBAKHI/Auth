import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { Highlight } from "./highlight"

describe("Highlight", () => {
  it("preserves unmatched text and marks every matching range", () => {
    const { container } = render(
      <p>
        <Highlight text="Alpha beta alpha" query="alpha" />
      </p>
    )

    expect(container.querySelector("p")).toHaveTextContent("Alpha beta alpha")
    expect(container.querySelectorAll("mark")).toHaveLength(2)
    expect(container.querySelectorAll("mark")[0]).toHaveTextContent("Alpha")
  })

  it("renders the original text when there is no match", () => {
    const { container } = render(<Highlight text="Users" query="roles" />)

    expect(container).toHaveTextContent("Users")
    expect(container.querySelector("mark")).toBeNull()
  })
})
