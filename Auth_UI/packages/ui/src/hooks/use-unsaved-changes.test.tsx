import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import * as React from "react"
import {
  Link,
  RouterProvider,
  createMemoryRouter,
  useLocation,
  useNavigate,
} from "react-router-dom"
import { beforeEach, describe, expect, it } from "vitest"

import i18n from "@authsystem/i18n"

import { useUnsavedChangesPrompt } from "./use-unsaved-changes"

function EditorGuard({
  initialDirty = false,
  initialSaving = false,
}: {
  initialDirty?: boolean
  initialSaving?: boolean
}) {
  const [isDirty, setDirty] = React.useState(initialDirty)
  const [isSaving, setSaving] = React.useState(initialSaving)
  const location = useLocation()
  const navigate = useNavigate()
  const prompt = useUnsavedChangesPrompt({ isDirty, isSaving })

  return (
    <>
      <output aria-label="location">{`${location.pathname}${location.search}`}</output>
      <button type="button" onClick={() => setDirty(true)}>
        edit
      </button>
      <button type="button" onClick={() => setSaving(false)}>
        save failed
      </button>
      <button
        type="button"
        onClick={() => {
          setDirty(false)
          setSaving(false)
        }}
      >
        save succeeded
      </button>
      <button type="button" onClick={() => navigate("/editor?tab=fr")}>
        switch tab
      </button>
      <Link to="/next">next page</Link>
      {prompt}
    </>
  )
}

function renderGuard(options?: {
  initialDirty?: boolean
  initialSaving?: boolean
}) {
  const router = createMemoryRouter(
    [
      { path: "/editor", element: <EditorGuard {...options} /> },
      { path: "/next", element: <h1>Next page</h1> },
    ],
    { initialEntries: ["/editor"] }
  )
  render(<RouterProvider router={router} />)
  return router
}

describe("useUnsavedChangesPrompt", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en")
  })

  it("allows clean navigation without a prompt", async () => {
    const router = renderGuard()

    await userEvent.click(screen.getByRole("link", { name: "next page" }))

    expect(await screen.findByRole("heading", { name: "Next page" })).toBeVisible()
    expect(router.state.location.pathname).toBe("/next")
  })

  it("cancels or explicitly discards a blocked dirty navigation", async () => {
    const router = renderGuard({ initialDirty: true })
    const user = userEvent.setup()

    await user.click(screen.getByRole("link", { name: "next page" }))
    expect(
      screen.getByRole("alertdialog", { name: "Discard changes?" })
    ).toBeVisible()

    await user.click(screen.getByRole("button", { name: "Cancel" }))
    expect(router.state.location.pathname).toBe("/editor")
    expect(screen.getByLabelText("location")).toHaveTextContent("/editor")

    await user.click(screen.getByRole("link", { name: "next page" }))
    await user.click(screen.getByRole("button", { name: "Discard" }))
    expect(await screen.findByRole("heading", { name: "Next page" })).toBeVisible()
  })

  it("allows same-page query state changes while dirty", async () => {
    const router = renderGuard({ initialDirty: true })

    await userEvent.click(screen.getByRole("button", { name: "switch tab" }))

    expect(router.state.location.pathname).toBe("/editor")
    expect(router.state.location.search).toBe("?tab=fr")
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
  })

  it("installs and removes the native beforeunload guard", async () => {
    renderGuard()

    const cleanEvent = new Event("beforeunload", { cancelable: true })
    window.dispatchEvent(cleanEvent)
    expect(cleanEvent.defaultPrevented).toBe(false)

    await userEvent.click(screen.getByRole("button", { name: "edit" }))
    const dirtyEvent = new Event("beforeunload", { cancelable: true })
    window.dispatchEvent(dirtyEvent)
    expect(dirtyEvent.defaultPrevented).toBe(true)

    await userEvent.click(screen.getByRole("button", { name: "save succeeded" }))
    const savedEvent = new Event("beforeunload", { cancelable: true })
    window.dispatchEvent(savedEvent)
    expect(savedEvent.defaultPrevented).toBe(false)
  })

  it("keeps discard disabled while saving, then exposes it after failure", async () => {
    const router = renderGuard({ initialDirty: true, initialSaving: true })

    await userEvent.click(screen.getByRole("link", { name: "next page" }))
    expect(
      screen.getByRole("alertdialog", { name: "Save in progress" })
    ).toBeVisible()
    expect(screen.getByRole("button", { name: "Discard" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled()

    fireEvent.click(screen.getByText("save failed"))
    await waitFor(() =>
      expect(
        screen.getByRole("alertdialog", { name: "Discard changes?" })
      ).toBeVisible()
    )
    expect(screen.getByRole("button", { name: "Discard" })).toBeEnabled()
    expect(router.state.location.pathname).toBe("/editor")
  })

  it("resumes a navigation blocked during a successful save", async () => {
    const router = renderGuard({ initialDirty: true, initialSaving: true })

    await userEvent.click(screen.getByRole("link", { name: "next page" }))
    fireEvent.click(screen.getByText("save succeeded"))

    await waitFor(() => expect(router.state.location.pathname).toBe("/next"))
    expect(screen.getByRole("heading", { name: "Next page" })).toBeVisible()
  })
})
