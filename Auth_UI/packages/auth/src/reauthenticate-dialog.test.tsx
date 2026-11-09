import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The window a signed-in person proves themself in (owner decision, 2026-11-09): what it
 * asks for comes from the server, one request proves it all or nothing, a success closes
 * it and hands back to the caller, and nothing signs out unless the person chooses the way
 * out, which still carries the full-proof reason through the sign-out (OI-215).
 *
 * Deliberate breaks, each verified red: the success not reaching `onProved`; the second
 * step left out of the body; the sign-in view shown while the password form is offered;
 * the full-proof reason set after the sign-out; the proof not saying it spent the app's code;
 * a spent recovery code not marked; a second hand-back; the way out open under a proof on its way.
 */

const mocks = vi.hoisted(() => ({
  logout: vi.fn(),
  get: vi.fn(),
  post: vi.fn(),
  toastSuccess: vi.fn(),
  start: vi.fn(),
  onCredential: null as ((credential: unknown) => Promise<{ message?: string | null; leaving?: boolean }>) | null,
  /** The reason as the sign-out found it. */
  flagAtSignOut: [] as Array<string | null>,
}))

vi.mock("@authsystem/api/client", () => ({ api: { GET: mocks.get, POST: mocks.post } }))
vi.mock("./auth-context", () => ({ useAuth: () => ({ logout: mocks.logout, user: { id: "user-1" } }) }))
vi.mock("sonner", () => ({ toast: { success: mocks.toastSuccess, error: vi.fn() } }))
vi.mock("./passkey/use-passkey-sign-in", () => ({
  usePasskeySignIn: ({ onCredential }: { onCredential: typeof mocks.onCredential }) => {
    mocks.onCredential = onCredential
    return {
      phase: "ready",
      message: null,
      retryable: false,
      suggestionSupported: false,
      start: mocks.start,
      retry: vi.fn(),
      confirm: vi.fn(),
    }
  },
}))

import { ReauthenticateDialog } from "./reauthenticate-dialog"
import { hasRecoveryCodeSignIn } from "./recovery-code-notice"
import { FULL_PROOF_KEY, isFullProofRequested, isPasskeySignInRequested } from "./sign-in-method"

const PASSWORD_ONLY = { passkey: false, password: true, emailCode: false, secondStep: "None", canProveInPlace: true }

const problem = (status: number, code: string) => ({
  data: undefined,
  error: { status, code, detail: `sentence of ${code}` },
  response: new Response(null, { status }),
})

function givenOptions(options: Partial<typeof PASSWORD_ONLY> = {}) {
  mocks.get.mockResolvedValue({ data: { ...PASSWORD_ONLY, ...options }, error: undefined })
}

function renderWindow(props: Partial<React.ComponentProps<typeof ReauthenticateDialog>> = {}) {
  const onOpenChange = vi.fn()
  const onProved = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const tree = (open: boolean) => (
    <QueryClientProvider client={client}>
      <ReauthenticateDialog open={open} onOpenChange={onOpenChange} onProved={onProved} {...props} />
    </QueryClientProvider>
  )
  const { rerender } = render(tree(true))
  return { onOpenChange, onProved, client, close: () => rerender(tree(false)) }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((settle) => {
    resolve = settle
  })
  return { promise, resolve }
}

const label = (key: string) => i18n.t(key)
const posted = (path: string) =>
  mocks.post.mock.calls.filter(([called]) => called === path).map(([, options]) => options?.body)

describe("ReauthenticateDialog", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    mocks.flagAtSignOut = []
    mocks.get.mockReset()
    mocks.post.mockReset().mockResolvedValue({ data: undefined, error: undefined })
    mocks.start.mockReset()
    mocks.toastSuccess.mockReset()
    mocks.onCredential = null
    mocks.logout.mockReset().mockImplementation(async () => {
      mocks.flagAtSignOut.push(window.sessionStorage.getItem(FULL_PROOF_KEY))
    })
    givenOptions()
  })

  // ── The proof ──

  it("takes the password where the person stands, and hands back to the caller", async () => {
    const { onOpenChange, onProved } = renderWindow()
    const user = userEvent.setup()

    expect(await screen.findByRole("dialog", { name: label("auth.reauthenticate.title") })).toBeInTheDocument()
    const field = await screen.findByLabelText(label("auth.password"))
    expect(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") })).toBeDisabled()
    await user.type(field, "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    expect(posted("/api/v1/auth/reauthentication")).toEqual([
      { password: "my password", emailCode: null, secondStepCode: null, useRecoveryCode: false },
    ])
    expect(onOpenChange).toHaveBeenCalledWith(false)
    expect(mocks.logout).not.toHaveBeenCalled()
  })

  // ── What a proof spent, for a caller holding a code of its own ──

  it("says when the proof spent the app's code: a code the caller holds will not pass now", async () => {
    givenOptions({ secondStep: "AuthenticatorApp" })
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.type(screen.getByLabelText(label("auth.twoFactorCode")), "123456")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledWith({ spentAppCode: true }))
  })

  it("says it did not when a password alone proved the person", async () => {
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledWith({ spentAppCode: false }))
  })

  it("marks a spent recovery code for the security page, and reads its count again", async () => {
    window.localStorage.clear()
    givenOptions({ secondStep: "AuthenticatorApp" })
    const { onProved, client } = renderWindow()
    const invalidate = vi.spyOn(client, "invalidateQueries")
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.useRecoveryCode") }))
    await user.type(screen.getByLabelText(label("auth.recoveryCode")), "RECOVERY-1")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledWith({ spentAppCode: false }))
    expect(hasRecoveryCodeSignIn("user-1")).toBe(true)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["me"] })
  })

  it("marks nothing when no recovery code was spent", async () => {
    window.localStorage.clear()
    const { onProved, client } = renderWindow()
    const invalidate = vi.spyOn(client, "invalidateQueries")
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    expect(hasRecoveryCodeSignIn("user-1")).toBe(false)
    expect(invalidate).not.toHaveBeenCalled()
  })

  // ── A proof on its way ──

  it("hands back once, when two proofs land together", async () => {
    givenOptions({ passkey: true })
    const form = deferred<{ data: undefined; error: undefined }>()
    const passkey = deferred<{ data: undefined; error: undefined }>()
    mocks.post.mockImplementation((path: string) =>
      path === "/api/v1/auth/reauthentication" ? form.promise : passkey.promise
    )
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))
    const answered = mocks.onCredential!({ id: "credential" })

    passkey.resolve({ data: undefined, error: undefined })
    await answered
    form.resolve({ data: undefined, error: undefined })

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(onProved).toHaveBeenCalledTimes(1)
  })

  it("keeps the way out shut while a proof is on its way", async () => {
    const pending = deferred<{ data: undefined; error: undefined }>()
    mocks.post.mockReturnValue(pending.promise)
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.signOutLink") }))

    expect(screen.queryByRole("button", { name: label("auth.reauthenticateAction") })).not.toBeInTheDocument()

    pending.resolve({ data: undefined, error: undefined })
    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
  })

  it("resumes nothing when the window has gone before the proof lands", async () => {
    const pending = deferred<{ data: undefined; error: undefined }>()
    mocks.post.mockReturnValue(pending.promise)
    const { onProved, close } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))
    close()

    pending.resolve({ data: undefined, error: undefined })
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(onProved).not.toHaveBeenCalled()
  })

  it("tells the person when the caller has nothing to resume", async () => {
    renderWindow({ onProved: undefined })
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(mocks.toastSuccess).toHaveBeenCalledWith(label("auth.reauthenticate.proved")))
  })

  it("says a wrong password in place, clears it, and stays open", async () => {
    mocks.post.mockResolvedValue(problem(400, "User.InvalidCurrentPassword"))
    const { onOpenChange, onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "a guess")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    expect(await screen.findByTestId("reauthenticate-message")).toHaveTextContent("sentence of User.InvalidCurrentPassword")
    expect(screen.getByLabelText(label("auth.password"))).toHaveValue("")
    expect(onProved).not.toHaveBeenCalled()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it("asks for the authenticator code with the password, and sends them together", async () => {
    givenOptions({ secondStep: "AuthenticatorApp" })
    const { onProved } = renderWindow()
    const user = userEvent.setup()
    const confirm = () => screen.getByRole("button", { name: label("auth.reauthenticate.confirm") })

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    expect(confirm()).toBeDisabled()
    await user.type(screen.getByLabelText(label("auth.twoFactorCode")), "123456")
    await user.click(confirm())

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    expect(posted("/api/v1/auth/reauthentication")).toEqual([
      { password: "my password", emailCode: null, secondStepCode: "123456", useRecoveryCode: false },
    ])
  })

  it("takes a recovery code in the authenticator's place", async () => {
    givenOptions({ secondStep: "AuthenticatorApp" })
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.useRecoveryCode") }))
    await user.type(screen.getByLabelText(label("auth.recoveryCode")), "RECOVERY-1")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    expect(posted("/api/v1/auth/reauthentication")).toEqual([
      { password: "my password", emailCode: null, secondStepCode: "RECOVERY-1", useRecoveryCode: true },
    ])
  })

  it("keeps a right password when only the second step was wrong", async () => {
    givenOptions({ secondStep: "AuthenticatorApp" })
    mocks.post.mockResolvedValue(problem(400, "User.InvalidTwoFactorCode"))
    renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.type(screen.getByLabelText(label("auth.twoFactorCode")), "000000")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await screen.findByTestId("reauthenticate-message")
    expect(screen.getByLabelText(label("auth.password"))).toHaveValue("my password")
    expect(screen.getByLabelText(label("auth.twoFactorCode"))).toHaveValue("")
  })

  it("emails the code of an account without a password, and takes it as the first proof", async () => {
    givenOptions({ password: false, emailCode: true })
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/reauthentication/email-code"
        ? { data: { maskedEmail: "o***@example.com", expiresAt: "2099-01-01T00:00:00Z", sendsRemaining: 2 } }
        : { data: undefined, error: undefined }
    )
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    expect(screen.queryByLabelText(label("auth.password"))).not.toBeInTheDocument()
    await user.click(await screen.findByRole("button", { name: label("auth.emailMeACode") }))
    await user.type(await screen.findByLabelText(label("profile.passkeys.emailCode")), "654321")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    expect(posted("/api/v1/auth/reauthentication")).toEqual([
      { password: null, emailCode: "654321", secondStepCode: null, useRecoveryCode: false },
    ])
  })

  it("asks for the emailed second factor after the password", async () => {
    givenOptions({ secondStep: "EmailCode" })
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/reauthentication/email-code"
        ? { data: { maskedEmail: "o***@example.com", expiresAt: "2099-01-01T00:00:00Z", sendsRemaining: 2 } }
        : { data: undefined, error: undefined }
    )
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.emailMeACode") }))
    await user.type(await screen.findByLabelText(label("profile.passkeys.emailCode")), "654321")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    await waitFor(() => expect(onProved).toHaveBeenCalledTimes(1))
    expect(posted("/api/v1/auth/reauthentication")).toEqual([
      { password: "my password", emailCode: null, secondStepCode: "654321", useRecoveryCode: false },
    ])
  })

  it("with email off, asks for a recovery code as the second step, with no way to ask for a code", async () => {
    givenOptions({ secondStep: "RecoveryCodeOnly" })
    renderWindow()

    expect(await screen.findByLabelText(label("auth.recoveryCode"))).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: label("auth.emailMeACode") })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: label("auth.useAuthenticatorCode") })).not.toBeInTheDocument()
  })

  // ── The passkey ──

  it("offers the passkey beside the password, and proves with its answer", async () => {
    givenOptions({ passkey: true })
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: label("auth.reauthenticate.withPasskey") }))
    expect(mocks.start).toHaveBeenCalledTimes(1)
    expect(screen.getByLabelText(label("auth.password"))).toBeInTheDocument()

    const outcome = await mocks.onCredential!({ id: "credential" })

    expect(outcome).toEqual({ leaving: true })
    expect(posted("/api/v1/auth/reauthentication/passkey")).toEqual([{ credential: { id: "credential" } }])
    expect(onProved).toHaveBeenCalledTimes(1)
  })

  it("says a passkey of another account in place", async () => {
    givenOptions({ passkey: true })
    mocks.post.mockResolvedValue(problem(400, "Passkey.NotThisAccount"))
    const { onProved } = renderWindow()

    await screen.findByRole("button", { name: label("auth.reauthenticate.withPasskey") })
    const outcome = await mocks.onCredential!({ id: "credential" })

    expect(outcome.message).toContain("sentence of Passkey.NotThisAccount")
    expect(onProved).not.toHaveBeenCalled()
  })

  it("asks for the passkey alone when the change needs two factors and it is the account's factor (T-C7)", async () => {
    givenOptions({ passkey: true })
    renderWindow({ passkey: true, description: "Adding a passkey needs a proof." })

    expect(await screen.findByRole("button", { name: label("auth.reauthenticate.withPasskey") })).toBeInTheDocument()
    expect(screen.getByText(label("auth.reauthenticate.passkeyBody"))).toBeInTheDocument()
    expect(screen.queryByText("Adding a passkey needs a proof.")).not.toBeInTheDocument()
    expect(screen.queryByLabelText(label("auth.password"))).not.toBeInTheDocument()
    // A browser that cannot run it, or a lost passkey, still has the way out.
    expect(screen.getByRole("button", { name: label("auth.reauthenticate.signOutLink") })).toBeInTheDocument()
  })

  it("asks for the password alone when the change needs the password itself", async () => {
    givenOptions({ passkey: true })
    renderWindow({ passwordOnly: true })

    expect(await screen.findByLabelText(label("auth.password"))).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: label("auth.reauthenticate.withPasskey") })).not.toBeInTheDocument()
  })

  // ── The way out ──

  it("signs nothing out by itself: the way out is the person's choice, carrying full proof", async () => {
    renderWindow()
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: label("auth.reauthenticate.signOutLink") }))
    expect(mocks.logout).not.toHaveBeenCalled()
    expect(screen.getByRole("dialog", { name: label("auth.reauthenticateTitle") })).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: label("auth.reauthenticateAction") }))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    // Written before the sign-out, and still there after it: the guard's redirect to the
    // sign-in page happens once the session is gone.
    expect(mocks.flagAtSignOut).toEqual(["1"])
    expect(isFullProofRequested()).toBe(true)
    expect(isPasskeySignInRequested()).toBe(false)
  })

  it("names the passkey on the way out for a passkey account, and asks the sign-in page to offer it first", async () => {
    givenOptions({ passkey: true })
    renderWindow({ passkey: true })
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: label("auth.reauthenticate.signOutLink") }))
    expect(screen.getByText(label("auth.reauthenticatePasskeyBody"))).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticateWithPasskeyAction") }))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    expect(mocks.flagAtSignOut).toEqual(["passkey"])
    expect(isPasskeySignInRequested()).toBe(true)
    // A code sign-in from there still proves the password too.
    expect(isFullProofRequested()).toBe(true)
  })

  it("answers the way out without a sign-out for a caller that would lose something by it", async () => {
    const onSignInAgain = vi.fn()
    renderWindow({ onSignInAgain })
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: label("auth.reauthenticate.signOutLink") }))
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticateAction") }))

    expect(onSignInAgain).toHaveBeenCalledTimes(1)
    expect(mocks.logout).not.toHaveBeenCalled()
    expect(isFullProofRequested()).toBe(true)
  })

  it("goes to the way out by itself when the account has nothing to prove here", async () => {
    givenOptions({ password: false, emailCode: false, passkey: false, canProveInPlace: false })
    renderWindow()

    expect(await screen.findByRole("dialog", { name: label("auth.reauthenticateTitle") })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: label("auth.reauthenticateAction") })).toBeInTheDocument()
  })

  it("goes to the way out when a passkey is demanded and the account holds none here", async () => {
    givenOptions({ passkey: false })
    renderWindow({ passkey: true })

    expect(await screen.findByRole("dialog", { name: label("auth.reauthenticateTitle") })).toBeInTheDocument()
  })

  it("goes to the way out when the server says the session cannot prove anything", async () => {
    mocks.get.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    renderWindow()

    expect(await screen.findByRole("dialog", { name: label("auth.reauthenticateTitle") })).toBeInTheDocument()
  })

  it("goes to the way out when the session ends under the proof", async () => {
    mocks.post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const { onProved } = renderWindow()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(label("auth.password")), "my password")
    await user.click(screen.getByRole("button", { name: label("auth.reauthenticate.confirm") }))

    expect(await screen.findByRole("dialog", { name: label("auth.reauthenticateTitle") })).toBeInTheDocument()
    expect(onProved).not.toHaveBeenCalled()
  })

  it("offers a retry when what it asks for could not be read, and the way out stays", async () => {
    mocks.get.mockRejectedValueOnce(new Error("offline"))
    renderWindow()
    const user = userEvent.setup()

    const failed = await screen.findByTestId("reauthenticate-options-failed")
    expect(screen.getByRole("button", { name: label("auth.reauthenticate.signOutLink") })).toBeInTheDocument()
    await user.click(failed.querySelector("button")!)

    expect(await screen.findByLabelText(label("auth.password"))).toBeInTheDocument()
  })

  it("asks nothing of a cancel", async () => {
    const { onOpenChange, onProved } = renderWindow()
    const user = userEvent.setup()

    await screen.findByLabelText(label("auth.password"))
    await user.click(screen.getByRole("button", { name: label("common.cancel") }))

    expect(onOpenChange).toHaveBeenCalledWith(false)
    expect(isFullProofRequested()).toBe(false)
    expect(mocks.logout).not.toHaveBeenCalled()
    expect(onProved).not.toHaveBeenCalled()
    expect(mocks.post).not.toHaveBeenCalled()
  })

  it("reads nothing while it is closed", () => {
    renderWindow({ open: false })

    expect(mocks.get).not.toHaveBeenCalled()
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
  })

  it("keeps the reason the caller gave for the proof", async () => {
    renderWindow({ description: "Why it is needed." })

    expect(await screen.findByText("Why it is needed.")).toBeInTheDocument()
  })
})
