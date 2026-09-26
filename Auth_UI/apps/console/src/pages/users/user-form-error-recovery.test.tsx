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

  // The two ways a rejected create names its field (ADR 0001): the pointers of
  // a result with several failures, and the published pointer of a single
  // failure's code. A field the form does not own must not bind.
  it.each([
    [
      "the pointers of several failures",
      {
        status: 400,
        code: "Email.InvalidFormat",
        errors: [
          { code: "Email.InvalidFormat", pointer: "#/email" },
          { code: "User.FirstNameTooLong", pointer: "#/futureInternalField" },
        ],
      },
    ],
    [
      "the published pointer of a single failure",
      { status: 400, code: "Email.InvalidFormat", detail: "raw backend validation" },
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

  it("puts every reason a password was refused under the control, not in the alert", async () => {
    // A password rule is a domain code, so the field-name mapping above would
    // never place it; the whole list must still land beside the control.
    post.mockResolvedValue({
      error: {
        status: 400,
        code: "Password.CommonPattern",
        detail: "Password contains a common pattern that is easy to guess.",
        errors: [
          { code: "Password.CommonPattern", pointer: "#/password" },
          { code: "Password.TooShort", pointer: "#/password" },
        ],
      },
    })
    renderDialog()

    await submitValidForm()

    const password = screen.getByLabelText(en.users.password)
    expect(
      await screen.findByText(
        "Password contains a common pattern that is easy to guess."
      )
    ).toBeVisible()
    // The API's sentence is for the first code only; the second is local copy.
    expect(
      screen.getByText(en.auth.passwordRefusals.tooShort)
    ).toBeVisible()
    expect(password).toHaveAttribute("aria-invalid", "true")
    await waitFor(() => expect(document.activeElement).toBe(password))
    expect(screen.queryByText(en.errors.feedback.title)).not.toBeInTheDocument()
  }, 10_000)
})
