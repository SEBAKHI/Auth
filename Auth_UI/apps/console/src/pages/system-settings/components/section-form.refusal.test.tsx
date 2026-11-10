import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, RouterProvider } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { SystemSettingsSection } from "../lib/sections"

/**
 * A value the section's rules refused says what to do about it, sometimes with a
 * command to copy (the passkey RP ID's retirement): it stays until it is
 * dismissed. Any other failure is a toast that goes, as before. That a toast with
 * these options really stays is settings-save-flow.spec.ts's, in a browser.
 */
const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
  post: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({
  api: { GET: mocks.get, PUT: mocks.put, POST: mocks.post },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: { language: "en" },
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("@authsystem/auth/require-permission", () => ({
  RequirePermission: ({ children }: { children: unknown }) => children,
}))

vi.mock("sonner", () => ({ toast: { error: mocks.toastError, success: vi.fn() } }))

const { SectionForm } = await import("./section-form")

const SECTION: SystemSettingsSection = {
  key: "Email",
  group: "Platform",
  editable: true,
  version: 1,
  rowVersion: null,
  fields: [
    {
      path: "Enabled",
      kind: "bool",
      effectiveValue: false,
      baselineValue: false,
      defaultValue: false,
      source: "File",
      readOnly: false,
      sensitive: false,
    },
  ],
}

function renderForm() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter([{ path: "/", element: <SectionForm section={SECTION} /> }])
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}

async function flipAndSave() {
  const user = userEvent.setup()
  await user.click(screen.getByRole("switch"))
  const saves = screen.getAllByRole("button", { name: "common.save" })
  await user.click(saves[saves.length - 1])
}

function refusedWith(code: string, detail: string) {
  mocks.put.mockResolvedValue({
    data: undefined,
    error: { status: 400, code, detail },
    response: { status: 400 },
  })
}

describe("a refused save", () => {
  beforeEach(() => {
    mocks.get.mockReset()
    mocks.put.mockReset()
    mocks.toastError.mockReset()
  })

  it("keeps a refused value's sentence until it is dismissed", async () => {
    refusedWith("SystemSettings.InvalidFieldValue", "The value for 'RpId' is invalid: restore it first.")
    renderForm()

    await flipAndSave()

    await waitFor(() =>
      expect(mocks.toastError).toHaveBeenCalledWith("The value for 'RpId' is invalid: restore it first.", {
        duration: Number.POSITIVE_INFINITY,
        closeButton: true,
      })
    )
  })

  it("leaves any other failure a toast that goes", async () => {
    refusedWith("SystemSettings.UnknownField", "Something failed.")
    renderForm()

    await flipAndSave()

    await waitFor(() => expect(mocks.toastError).toHaveBeenCalledTimes(1))
    expect(mocks.toastError.mock.calls[0]?.[1]).toBeUndefined()
  })
})
