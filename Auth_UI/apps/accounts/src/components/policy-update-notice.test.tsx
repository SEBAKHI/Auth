import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const get = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => get(...args) },
}))

import { PolicyUpdateNotice } from "./policy-update-notice"

const STORAGE_KEY = "privacy.acknowledgedPolicyVersion"

function renderNotice() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <PolicyUpdateNotice />
    </QueryClientProvider>
  )
}

describe("PolicyUpdateNotice", () => {
  beforeEach(() => {
    localStorage.clear()
    get.mockReset().mockResolvedValue({ data: { version: "2026.08" } })
  })

  it("silently adopts the first published version", async () => {
    renderNotice()

    await waitFor(() =>
      expect(localStorage.getItem(STORAGE_KEY)).toBe("2026.08")
    )
    expect(screen.queryByText("Our privacy policy has been updated.")).toBeNull()
  })

  it("shows a changed version and persists dismissal", async () => {
    localStorage.setItem(STORAGE_KEY, "2026.07")
    renderNotice()

    expect(
      await screen.findByText("Our privacy policy has been updated.")
    ).toBeVisible()
    await userEvent.click(screen.getByRole("button", { name: "Dismiss" }))
    expect(screen.queryByText("Our privacy policy has been updated.")).toBeNull()
    expect(localStorage.getItem(STORAGE_KEY)).toBe("2026.08")
  })
})
