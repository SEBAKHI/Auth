import { render, screen } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it } from "vitest"

import { RecordLink } from "./record-link"

function renderLink(href: string | undefined) {
  return render(
    <MemoryRouter>
      <RecordLink href={href} className="text-start">
        <p>Row name</p>
      </RecordLink>
    </MemoryRouter>
  )
}

describe("RecordLink", () => {
  it("renders a real anchor carrying the destination", () => {
    renderLink("/users/abc")

    const link = screen.getByRole("link", { name: "Row name" })
    expect(link).toHaveAttribute("href", "/users/abc")
    // A link, not a button: this is what gives the row Ctrl-click, middle
    // click, "copy link address" and a visible target on hover.
    expect(screen.queryByRole("button")).not.toBeInTheDocument()
  })

  it("renders the same content as plain text when there is nowhere to go", () => {
    renderLink(undefined)

    expect(screen.getByText("Row name")).toBeInTheDocument()
    expect(screen.queryByRole("link")).not.toBeInTheDocument()
  })

  it("keeps the caller's layout classes in both states, so the column cannot shift", () => {
    const { unmount } = renderLink("/users/abc")
    const withHref = screen.getByRole("link").className
    unmount()

    renderLink(undefined)
    const withoutHref = screen.getByText("Row name").parentElement?.className

    const layoutOnly = (classes: string | undefined) =>
      (classes ?? "")
        .split(/\s+/)
        .filter((entry) => entry && entry !== "hover:underline")
        .sort()
    expect(layoutOnly(withoutHref)).toEqual(layoutOnly(withHref))
  })

  it("underlines on hover only where there is somewhere to go", () => {
    // An underline is a promise of a destination. The Accounts app mounts the
    // shared organization page with no href builders, so EVERY name there took
    // the fallback - and used to underline under the cursor and do nothing.
    const { unmount } = renderLink("/users/abc")
    expect(screen.getByRole("link").className).toContain("hover:underline")
    unmount()

    renderLink(undefined)
    expect(
      screen.getByText("Row name").parentElement?.className
    ).not.toContain("hover:underline")
  })
})
