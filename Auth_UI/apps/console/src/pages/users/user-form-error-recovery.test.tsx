import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { UserFormDialog } from "./user-form-dialog"
import { en } from "@authsystem/i18n/locales/en"

const { post } = vi.hoisted(() => ({ post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    POST: (...args: unknown[]) => post(...args),
    PUT: vi.fn(),
  },
}))

function renderDialog() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })

  render(
    <QueryClientProvider client={queryClient}>
      <UserFormDialog open onOpenChange={vi.fn()} />
    </QueryClientProvider>
  )
}

async function submitValidForm() {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText(en.common.email), "new@example.test")
  await user.type(screen.getByLabelText(en.users.password), "SafePassword123!")
  await user.type(screen.getByLabelText(en.users.firstName), "New")
  await user.type(screen.getByLabelText(en.users.lastName), "Operator")
  await user.click(screen.getByRole("button", { name: en.common.create }))
}

describe("UserFormDialog API recovery", () => {
  beforeEach(() => {
    post.mockReset()
  })

  // The two payload shapes a rejected create can arrive in. The array is what
  // this endpoint emits (FluentValidation names the property as the ErrorOr
  // code); the dictionary is ASP.NET's model-state 400, which endpoints whose
  // request DTOs carry DataAnnotations still produce.
  it.each([
    [
      "a multi-error ProblemDetails array",
      {
        status: 400,
        title: "Email",
        errors: [
          { code: "Email", description: "raw backend validation" },
          { code: "FutureInternalField", description: "must not bind" },
        ],
      },
    ],
    [
      "a single-error ProblemDetails title",
      { status: 400, title: "Email", detail: "raw backend validation" },
    ],
    [
      "an ASP.NET model-state dictionary",
      {
        status: 400,
        errors: {
          Email: ["raw backend validation"],
          FutureInternalField: ["must not bind"],
        },
      },
    ],
  ])("places the rejected field beside the control and focuses it: %s", async (_shape, error) => {
    post.mockResolvedValue({ error })
    renderDialog()

    await submitValidForm()

    const email = screen.getByLabelText(en.common.email)
    expect(
      await screen.findByText(en.errors.feedback.fieldInvalid)
    ).toBeVisible()
    expect(email).toHaveAttribute("aria-invalid", "true")
    expect(email.closest('[data-slot="field"]')).toHaveAttribute(
      "data-invalid",
      "true"
    )
    await waitFor(() => expect(document.activeElement).toBe(email))
    expect(screen.queryByText("raw backend validation")).not.toBeInTheDocument()
  }, 10_000)

  it("offers a replay of the same values for a transient failure", async () => {
    const failure = { status: 503, detail: "raw infrastructure detail" }
    post.mockResolvedValue({ error: failure })
    renderDialog()

    await submitValidForm()

    expect(await screen.findByText(en.errors.feedback.title)).toBeVisible()
    expect(screen.getByText(en.errors.feedback.server)).toBeVisible()
    fireEvent.click(
      screen.getByRole("button", { name: en.errors.feedback.retry })
    )
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2))
    expect(post.mock.calls[1]).toEqual(post.mock.calls[0])
  }, 10_000)
})
