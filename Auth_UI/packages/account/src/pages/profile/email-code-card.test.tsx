import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"

/**
 * S24 PR B: the emailed code as the account's second factor, on the profile's
 * security tab. Switching it on emails a code that proves the mailbox, and over an
 * authenticator app it first says the app will be replaced; switching it off asks
 * for a code emailed for that, or a recovery code, after naming both effects. A
 * card that cannot act says why instead of offering a button that would fail.
 */
const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  logout: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: mocks.post, GET: vi.fn(), DELETE: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ logout: mocks.logout }),
}))

vi.mock("sonner", () => ({
  toast: { error: mocks.toastError, success: vi.fn() },
}))

const { EmailCodeCard } = await import("./email-code-card")

const SENT = {
  data: { expiresAt: new Date(Date.now() + 600_000).toISOString(), maskedEmail: "o***r@example.com", sendsRemaining: 2 },
  error: undefined,
}

function renderCard(me: Partial<Schemas["UserDto"]>) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <EmailCodeCard
        me={{ email: "owner@example.com", hasPassword: true, emailCodeAvailable: true, ...me } as Schemas["UserDto"]}
      />
    </QueryClientProvider>
  )
}

const posted = (path: string) =>
  mocks.post.mock.calls.filter(([called]) => called === path).map(([, options]) => options)

describe("EmailCodeCard", () => {
  beforeEach(() => {
    mocks.post.mockReset()
    mocks.toastError.mockReset()
  })

  it("without a password, says why and offers no working button", () => {
    renderCard({ hasPassword: false })

    expect(screen.getByTestId("email-code-reason")).toHaveTextContent("profile.emailCodeNeedsPassword")
    expect(screen.getByRole("button", { name: "profile.emailCodeTurnOn" })).toBeDisabled()
  })

  it("against an API without the feature, offers nothing: its routes would answer 404 (review of #107)", () => {
    renderCard({ emailCodeAvailable: undefined })

    expect(screen.queryByRole("button", { name: "profile.emailCodeTurnOn" })).not.toBeInTheDocument()
  })

  it("for an administrator a policy keeps on the app, says why and offers no turn-on (review of #107)", () => {
    renderCard({ totpEnabled: true, twoFactorRequiredByPolicy: true })

    expect(screen.getByTestId("email-code-reason")).toHaveTextContent("profile.twoFactorRequiredByPolicy")
    expect(screen.queryByRole("button", { name: "profile.emailCodeTurnOn" })).not.toBeInTheDocument()
  })

  it("while email sending is off, says why and offers no working button", () => {
    renderCard({ emailCodeAvailable: false })

    expect(screen.getByTestId("email-code-reason")).toHaveTextContent("profile.emailCodeUnavailable")
    expect(screen.getByRole("button", { name: "profile.emailCodeTurnOn" })).toBeDisabled()
  })

  it("turns on with the emailed code, and shows the new recovery codes once", async () => {
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/2fa/email/enable"
        ? SENT
        : { data: { recoveryCodes: ["AAAA-BBBB", "CCCC-DDDD"] }, error: undefined }
    )
    const user = userEvent.setup()
    renderCard({})

    await user.click(screen.getByRole("button", { name: "profile.emailCodeTurnOn" }))

    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("profile.emailCodeConfirmTitle")).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText("auth.emailCode"), "123456")
    await user.click(within(dialog).getByRole("button", { name: "auth.verify" }))

    expect(posted("/api/v1/auth/2fa/email/confirm")).toEqual([{ body: { code: "123456" } }])
    expect(await screen.findByText(/AAAA-BBBB/)).toBeInTheDocument()
  })

  it("over an authenticator app, warns that it replaces the app before anything is sent", async () => {
    mocks.post.mockResolvedValue(SENT)
    const user = userEvent.setup()
    renderCard({ totpEnabled: true })

    await user.click(screen.getByRole("button", { name: "profile.emailCodeTurnOn" }))

    const warning = await screen.findByRole("alertdialog")
    expect(within(warning).getByText("profile.emailCodeReplaceTitle")).toBeInTheDocument()
    expect(within(warning).getByText("profile.emailCodeReplaceBody")).toBeInTheDocument()
    expect(mocks.post).not.toHaveBeenCalled()

    await user.click(within(warning).getByRole("button", { name: "profile.emailCodeTurnOn" }))

    expect(posted("/api/v1/auth/2fa/email/enable")).toHaveLength(1)
  })

  it("offers no resend once no send is left: it could only be refused (OI-220 (1))", async () => {
    // Review of #107: the card ignored sendsRemaining, so the fourth click was a 403.
    mocks.post.mockResolvedValue({ data: { ...SENT.data, sendsRemaining: 0 }, error: undefined })
    const user = userEvent.setup()
    renderCard({ emailCodeEnabled: true, twoFactorEnabled: true })

    await user.click(screen.getByRole("button", { name: "profile.emailCodeTurnOff" }))
    const dialog = await screen.findByRole("alertdialog")
    await user.click(within(dialog).getByRole("button", { name: "auth.emailMeACode" }))

    expect(await within(dialog).findByLabelText("auth.emailCode")).toBeInTheDocument()
    expect(within(dialog).queryByRole("button", { name: /resend/i })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole("button", { name: "auth.emailMeACode" })).not.toBeInTheDocument()
  })

  it("turns off after naming both effects, with a code emailed for it", async () => {
    mocks.post.mockImplementation(async (_path: string, options: { body: Record<string, unknown> }) =>
      options.body.code ? { data: undefined, error: undefined } : SENT
    )
    const user = userEvent.setup()
    renderCard({ emailCodeEnabled: true, twoFactorEnabled: true })

    await user.click(screen.getByRole("button", { name: "profile.emailCodeTurnOff" }))

    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("profile.emailCodeDisableBody")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "auth.emailMeACode" }))
    await user.type(within(dialog).getByLabelText("auth.emailCode"), "654321")
    await user.click(within(dialog).getByRole("button", { name: "profile.emailCodeTurnOff" }))

    expect(posted("/api/v1/auth/2fa/email/disable")).toEqual([
      { body: {} },
      { body: { code: "654321", useRecoveryCode: false } },
    ])
  })

  it("turns off with a recovery code when the mailbox is out of reach", async () => {
    mocks.post.mockResolvedValue({ data: undefined, error: undefined })
    const user = userEvent.setup()
    renderCard({ emailCodeEnabled: true, twoFactorEnabled: true })

    await user.click(screen.getByRole("button", { name: "profile.emailCodeTurnOff" }))
    const dialog = await screen.findByRole("alertdialog")
    await user.click(within(dialog).getByRole("button", { name: "auth.useRecoveryCode" }))
    await user.type(within(dialog).getByLabelText("auth.recoveryCode"), "ABCD-EFGH")
    await user.click(within(dialog).getByRole("button", { name: "profile.emailCodeTurnOff" }))

    expect(posted("/api/v1/auth/2fa/email/disable")).toEqual([
      { body: { code: "ABCD-EFGH", useRecoveryCode: true } },
    ])
  })

  it("a stale sign-in asks to sign in again instead of a toast", async () => {
    mocks.post.mockResolvedValue({
      data: undefined,
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "sign in again" },
    })
    const user = userEvent.setup()
    renderCard({})

    await user.click(screen.getByRole("button", { name: "profile.emailCodeTurnOn" }))

    expect(await screen.findByText("profile.twoFactorChangeNeedsRecentSignIn")).toBeInTheDocument()
    expect(mocks.toastError).not.toHaveBeenCalled()
  })
})
