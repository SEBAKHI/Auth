import { render, screen } from "@testing-library/react"
import { createMemoryRouter, RouterProvider, useLocation } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { MfaRequirement } from "@authsystem/api/mfa-requirement"

const auth = vi.hoisted(() => ({ mfaRequirement: "none" as MfaRequirement }))
vi.mock("./auth-context", () => ({ useAuth: () => auth }))

import { RequireMfaSatisfied, TWO_FACTOR_REQUIRED_PATH } from "./require-mfa-satisfied"

/** Reports where the router ended up, and what it carried there. */
function Landing() {
  const location = useLocation()
  return (
    <div data-testid="landing">
      {location.pathname}
      {"|"}
      {JSON.stringify(location.state ?? null)}
    </div>
  )
}

/**
 * The guard wraps the shell as in both apps: an exempt profile, a guarded page,
 * and the page it sends to, outside it.
 */
function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { path: TWO_FACTOR_REQUIRED_PATH, element: <Landing /> },
      {
        element: <RequireMfaSatisfied />,
        children: [
          { path: "/users", element: <div>users page</div> },
          {
            path: "/profile",
            element: <div>profile page</div>,
            handle: { crumb: { titleKey: "profile", href: "/profile" }, mfaExempt: true },
          },
          {
            path: "/settings",
            element: <div>settings page</div>,
            // A handle that is not the exemption must not pass for one.
            handle: { mfaExempt: "yes" },
          },
        ],
      },
    ],
    { initialEntries: [path] }
  )

  render(<RouterProvider router={router} />)
}

describe("RequireMfaSatisfied", () => {
  beforeEach(() => {
    auth.mfaRequirement = "none"
  })

  it("lets every page through when nothing is required", () => {
    renderAt("/users")

    expect(screen.getByText("users page")).toBeInTheDocument()
  })

  it.each(["enroll", "step_up", "reauthenticate"] as const)(
    "sends a guarded page to the two-factor page when %s is required, carrying where it was going",
    (requirement) => {
      auth.mfaRequirement = requirement
      renderAt("/users")

      expect(screen.queryByText("users page")).not.toBeInTheDocument()
      const landing = screen.getByTestId("landing")
      expect(landing).toHaveTextContent(`${TWO_FACTOR_REQUIRED_PATH}|`)
      expect(landing).toHaveTextContent('"pathname":"/users"')
    }
  )

  it("lets an exempt page through while something is required", () => {
    // The profile: enrolment and the security tab live there.
    auth.mfaRequirement = "enroll"
    renderAt("/profile")

    expect(screen.getByText("profile page")).toBeInTheDocument()
  })

  it("takes only a true mfaExempt as the exemption", () => {
    auth.mfaRequirement = "step_up"
    renderAt("/settings")

    expect(screen.queryByText("settings page")).not.toBeInTheDocument()
    expect(screen.getByTestId("landing")).toHaveTextContent(TWO_FACTOR_REQUIRED_PATH)
  })
})
