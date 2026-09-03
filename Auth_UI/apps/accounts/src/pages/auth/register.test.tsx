import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { post, get, interstitial } = vi.hoisted(() => ({
  post: vi.fn(),
  get: vi.fn(),
  interstitial: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({
  api: {
    POST: (...args: unknown[]) => post(...args),
    GET: (...args: unknown[]) => get(...args),
  },
}))
vi.mock("@authsystem/api/env", () => ({
  privacyPolicyUrl: () => "/privacy/en",
}))
vi.mock("@authsystem/auth/login-completion", () => ({
  useLoginCompletion: () => ({ interstitial, complete: vi.fn() }),
}))
vi.mock("@authsystem/auth/external/external-providers", () => ({
  ExternalProviders: () => null,
}))
// Chrome only: the real layout drags in the theme and language toggles and
// their provider stack. Keep the parts the form renders into.
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    children,
    footer,
    pageFooter,
  }: {
    title: string
    children: React.ReactNode
    footer?: React.ReactNode
    pageFooter?: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {children}
      {footer}
      {pageFooter}
    </div>
  ),
}))

import { RegisterPage } from "./register"

const POLICY = {
  minimumLength: 8,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireSpecialCharacter: true,
}

function renderPage() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <RegisterPage />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/^email$/i), "new@example.test")
  await user.type(screen.getByLabelText(/^first name$/i), "New")
  await user.type(screen.getByLabelText(/^last name$/i), "Person")
  await user.type(screen.getByLabelText(/^password$/i), "NewPass1!")
  await user.type(screen.getByLabelText(/^confirm password$/i), "NewPass1!")
  await user.click(screen.getByRole("button", { name: /create account/i }))
}

describe("RegisterPage", () => {
  beforeEach(() => {
    post.mockReset()
    interstitial.mockReset()
    get.mockResolvedValue({ data: POLICY })
  })

  it("shows the live policy under the password field before anything is typed", async () => {
    renderPage()

    const list = await screen.findByRole("list", {
      name: /password requirements/i,
    })
    expect(within(list).getAllByRole("listitem")).toHaveLength(5)
    expect(list).toHaveTextContent("At least 8 characters")
    expect(list.querySelectorAll('[data-met="true"]')).toHaveLength(0)
  })

  it("puts every reason the server refuses under the field, not one in a toast", async () => {
    post.mockResolvedValue({
      error: {
        status: 400,
        title: "Password.CommonPattern",
        detail: "Password contains a common pattern that is easy to guess.",
        errors: [
          {
            code: "Password.CommonPattern",
            description:
              "Password contains a common pattern that is easy to guess.",
          },
          {
            code: "Password.TooShort",
            description: "Password must be at least 12 characters long.",
          },
        ],
      },
    })
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    expect(
      await screen.findByText(
        "Password contains a common pattern that is easy to guess."
      )
    ).toBeVisible()
    expect(
      screen.getByText("Password must be at least 12 characters long.")
    ).toBeVisible()
    const password = screen.getByLabelText(/^password$/i)
    expect(password).toHaveAttribute("aria-invalid", "true")
    await waitFor(() => expect(document.activeElement).toBe(password))
    expect(interstitial).not.toHaveBeenCalled()
  }, 15_000)

  it("hands a successful registration to the verification step", async () => {
    post.mockResolvedValue({
      data: {
        message: "Check your inbox.",
        maskedEmail: "n***@example.test",
        verificationCodeExpiresAt: "2026-09-03T10:00:00Z",
      },
    })
    const user = userEvent.setup()
    renderPage()
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    await waitFor(() =>
      expect(interstitial).toHaveBeenCalledWith(
        "/verify-email",
        expect.objectContaining({
          email: "new@example.test",
          maskedEmail: "n***@example.test",
        })
      )
    )
    expect(post).toHaveBeenCalledWith(
      "/api/v1/Auth/register",
      expect.objectContaining({
        body: expect.objectContaining({
          email: "new@example.test",
          password: "NewPass1!",
        }),
      })
    )
  }, 15_000)
})
