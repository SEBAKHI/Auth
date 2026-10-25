import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The authenticator app in place of the emailed code (S24, OI-220 (6)): the shared
 * enrollment asks for the proof that the emailed code may go, and never for two
 * mailbox codes. Used by the profile and by the page that stands between a platform
 * administrator and the console, so it is pinned here, once.
 */
vi.mock("@authsystem/api/client", () => ({
  api: { POST: vi.fn(), GET: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }))

vi.mock("./auth-context", () => ({ useAuth: () => ({ logout: vi.fn() }) }))

import { api } from "@authsystem/api/client"

import { TwoFactorEnrollment } from "./two-factor-enrollment"

const post = api.POST as unknown as ReturnType<typeof vi.fn>

const SETUP = {
  secret: "JBSWY3DPEHPK3PXP",
  qrCodeUri: "otpauth://totp/Example:owner@example.com?secret=JBSWY3DPEHPK3PXP",
  manualEntryKey: "JBSW Y3DP EHPK 3PXP",
}

const problem = (status: number, code: string) => ({
  error: { status, code, detail: `server sentence for ${code}` },
})

const proofSent = (sendsRemaining: number) => ({
  data: {
    maskedEmail: "o***r@example.com",
    expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
    sendsRemaining,
  },
})

function givenApi(setup: Record<string, unknown>, enable: () => unknown, proof: () => unknown = () => proofSent(2)) {
  post.mockImplementation(async (path: string) => {
    if (path === "/api/v1/auth/2fa/setup") return { data: setup }
    if (path === "/api/v1/auth/2fa/email/disable") return proof()
    if (path === "/api/v1/auth/2fa/enable") return enable()
    return { data: undefined }
  })
}

const enableBodies = () =>
  post.mock.calls
    .filter(([path]) => path === "/api/v1/auth/2fa/enable")
    .map(([, options]) => (options as { body: unknown }).body)

async function startSetup(user: ReturnType<typeof userEvent.setup>, replacesEmailCode: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <TwoFactorEnrollment
        account={{ id: "u-1", email: "owner@example.com", emailConfirmed: true }}
        replacesEmailCode={replacesEmailCode}
        onEnabled={vi.fn()}
      />
    </QueryClientProvider>
  )
  await user.click(screen.getByRole("button", { name: "profile.enableTwoFactor" }))
  await screen.findByLabelText("auth.twoFactorCode")
}

beforeEach(() => {
  post.mockReset()
})

describe("TwoFactorEnrollment over the emailed code", () => {
  it("with an emailed proof, asks the mailbox once: no first-factor code beside it", async () => {
    givenApi({ ...SETUP, emailCodeRequired: true }, () => ({ data: { recoveryCodes: ["AAAA-1111"] } }))
    const user = userEvent.setup()
    await startSetup(user, true)

    expect(screen.queryByLabelText("profile.twoFactorEmailCode")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" }))
    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/email/disable", { body: {} })
    await user.type(await screen.findByLabelText("profile.replaceEmailCodeProof"), "111111")
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    await waitFor(() =>
      expect(enableBodies()).toEqual([
        { code: "123456", replacementProofCode: "111111", replacementProofIsRecoveryCode: false },
      ])
    )
  })

  it("with a recovery code as the proof, still asks the first-factor mailbox code while it is required", async () => {
    givenApi({ ...SETUP, emailCodeRequired: true }, () => ({ data: { recoveryCodes: ["AAAA-1111"] } }))
    const user = userEvent.setup()
    await startSetup(user, true)

    await user.click(screen.getByRole("button", { name: "auth.useRecoveryCode" }))
    await user.type(screen.getByLabelText("auth.recoveryCode"), "ABCD-2345")
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")

    expect(screen.getByRole("button", { name: "auth.verify" })).toBeDisabled()
    await user.type(screen.getByLabelText("profile.twoFactorEmailCode"), "654321")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    await waitFor(() =>
      expect(enableBodies()).toEqual([
        {
          code: "123456",
          emailCode: "654321",
          replacementProofCode: "ABCD-2345",
          replacementProofIsRecoveryCode: true,
        },
      ])
    )
  })

  it("learns the replacement from the server when the caller did not know", async () => {
    givenApi(SETUP, () => problem(400, "TwoFactor.ReplacementProofRequired"))
    const user = userEvent.setup()
    await startSetup(user, false)

    expect(screen.queryByLabelText("profile.replaceEmailCodeProof")).not.toBeInTheDocument()
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    expect(await screen.findByLabelText("profile.replaceEmailCodeProof")).toBeInTheDocument()
    expect(screen.getByText("profile.replaceEmailCodeTitle")).toBeInTheDocument()
  })

  it("offers no resend of the proof once no send is left (OI-220 (1))", async () => {
    givenApi(SETUP, () => ({ data: { recoveryCodes: [] } }), () => proofSent(0))
    const user = userEvent.setup()
    await startSetup(user, true)

    await user.click(screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" }))

    await waitFor(() =>
      expect(screen.queryByRole("button", { name: /profile\.twoFactorEmailCode(Send|Resend)/ })).not.toBeInTheDocument()
    )
    expect(screen.getByLabelText("profile.replaceEmailCodeProof")).toBeEnabled()
  })
})
