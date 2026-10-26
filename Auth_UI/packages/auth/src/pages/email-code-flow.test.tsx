import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type * as React from "react"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

/**
 * OI-215: everything after a code was sent. Before the code matched, the screen
 * is the same for every address and asks the server nothing; after it, the
 * server's next step decides the screen. Every exit goes through the one
 * completion rule, and the method is remembered as one word.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  signInWithEmailCode: vi.fn(),
  completeRegistration: vi.fn(),
  recoverWithEmailCode: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => mocks.post(...args) },
}))
vi.mock("../auth-context", () => ({
  useAuth: () => ({
    signInWithEmailCode: mocks.signInWithEmailCode,
    completeRegistration: mocks.completeRegistration,
    recoverWithEmailCode: mocks.recoverWithEmailCode,
  }),
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: null,
    complete: mocks.complete,
    challenge: mocks.challenge,
  }),
}))
vi.mock("@authsystem/api/password-policy", () => ({
  usePasswordPolicy: () => ({ policy: undefined }),
}))
vi.mock("../flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: null,
    layout: { appName: null, appLogoUrl: null, securedBy: null },
  }),
}))
vi.mock("sonner", () => ({
  toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() },
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    subtitle,
    children,
  }: {
    title: string
    subtitle?: string
    children: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {subtitle ? <p>{subtitle}</p> : null}
      {children}
    </div>
  ),
}))
vi.mock("@authsystem/ui/common/otp-input", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@authsystem/ui/common/otp-input")>()
  return {
    ...actual,
    OtpInput: ({
      value,
      onChange,
      onComplete,
      label,
    }: {
      value: string
      onChange: (value: string) => void
      onComplete?: (value: string) => void
      label: string
    }) => (
      <input
        aria-label={label}
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
          if (event.target.value.length === actual.OTP_CODE_LENGTH) {
            onComplete?.(event.target.value)
          }
        }}
      />
    ),
  }
})

import { LAST_SIGN_IN_METHOD_KEY, challengeEnded } from "../sign-in-method"
import { EmailCodeFlow } from "./email-code-flow"

const PENDING = {
  pendingId: "handle-1",
  email: "jane@one.example",
  maskedEmail: "j***@one.example",
  expiresAt: "2099-09-10T10:05:00.000Z",
}

const ok = (data: object) => ({
  data,
  response: new Response(null, { status: 200 }),
})
const problem = (status: number, code: string) => ({
  error: { status, code, detail: `refused: ${code}` },
})

function answerVerify(body: object | (() => object)) {
  mocks.post.mockImplementation(async (path: string) => {
    if (path !== "/api/v1/Auth/email-code/verify") return problem(500, "x")
    return typeof body === "function" ? body() : ok(body)
  })
}

function renderFlow(
  props: Partial<React.ComponentProps<typeof EmailCodeFlow>> = {}
) {
  const handlers = {
    onChangeAddress: vi.fn(),
    onPasswordRequired: vi.fn(),
  }
  const view = render(
    <MemoryRouter>
      <EmailCodeFlow
        pending={PENDING}
        staySignedIn={false}
        requireFullProof={false}
        {...handlers}
        {...props}
      />
    </MemoryRouter>
  )
  return { ...view, ...handlers }
}

async function typeCode(code = "123456") {
  await userEvent.setup().type(screen.getByLabelText(/verification code/i), code)
}

/** Every value in a storage area, whatever its key. */
function storedValues(storage: Storage): string[] {
  const values: string[] = []
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index)
    if (key !== null) values.push(storage.getItem(key) ?? "")
  }
  return values
}

describe("EmailCodeFlow", () => {
  beforeEach(() => {
    window.localStorage.clear()
    window.sessionStorage.clear()
    for (const mock of Object.values(mocks)) mock.mockReset()
  })

  it("shows the same screen whatever the server chose, and asks the server nothing on arrival", () => {
    // The clock is frozen so the countdown reads the same in both renders.
    vi.useFakeTimers({ toFake: ["Date"] })
    vi.setSystemTime(new Date("2099-09-10T10:00:00.000Z"))
    try {
      // The start answers the same shape for every address, but its values are
      // the server's: a handle and a masked address that could differ by what
      // the address is. Nothing of them reaches the screen — only what the
      // visitor typed, and the expiry.
      const first = renderFlow({
        pending: { ...PENDING, pendingId: "handle-for-an-account", maskedEmail: "j***e@one.example" },
      })
      const forOne = first.container.innerHTML
      first.unmount()
      const second = renderFlow({
        pending: { ...PENDING, pendingId: "handle-for-no-account", maskedEmail: "x***@one.example" },
      })

      expect(second.container.innerHTML).toBe(forOne)
      expect(forOne).not.toContain("handle-for")
      expect(forOne).not.toContain("***")
      expect(
        screen.getByText(
          "We sent a message to jane@one.example. Enter the six-digit code in it."
        )
      ).toBeInTheDocument()
      expect(mocks.post).not.toHaveBeenCalled()
    } finally {
      vi.useRealTimers()
    }
  })

  it("signs in once the code matched, and remembers the method as one word", async () => {
    answerVerify({ step: "signIn" })
    mocks.signInWithEmailCode.mockResolvedValue({
      status: "authenticated",
      requiresPasswordChange: false,
    })
    renderFlow({ staySignedIn: true })

    await typeCode()

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    expect(mocks.signInWithEmailCode).toHaveBeenCalledWith({
      pendingId: "handle-1",
      otp: "123456",
      requireFullProof: false,
      staySignedIn: true,
    })
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBe("code")
    // Nothing else lands in the browser: never the address, never the code.
    expect(storedValues(window.localStorage)).toEqual(["code"])
    expect(storedValues(window.sessionStorage)).toEqual([])
  })

  it("sends the full-proof reason with the code", async () => {
    answerVerify({ step: "signIn" })
    mocks.signInWithEmailCode.mockResolvedValue({
      status: "nextStep",
      nextStep: { step: "passwordRequired", passwordRequired: true, nameRequired: true },
    })
    const { onPasswordRequired } = renderFlow({ requireFullProof: true })

    await typeCode()

    await waitFor(() =>
      expect(onPasswordRequired).toHaveBeenCalledWith("jane@one.example")
    )
    expect(mocks.signInWithEmailCode.mock.calls[0][0].requireFullProof).toBe(true)
    // No session yet: nothing is remembered.
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBeNull()
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("hands a second step to the completion rule, and remembers the method when it ends", async () => {
    answerVerify({ step: "signIn" })
    mocks.signInWithEmailCode.mockResolvedValue({
      status: "twoFactorRequired",
      challengeToken: "challenge-1",
      methods: ["totp", "recovery"],
    })
    renderFlow()

    await typeCode()

    await waitFor(() =>
      expect(mocks.challenge).toHaveBeenCalledWith("challenge-1", [
        "totp",
        "recovery",
      ])
    )
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBeNull()
    challengeEnded("challenge-1")
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBe("code")
  })

  it.each([
    [true, true, ["first name", "last name", "password"]],
    [true, false, ["password"]],
    [false, true, ["first name", "last name"]],
  ])(
    "asks a new account for what the server names (password %s, name %s)",
    async (passwordRequired, nameRequired, shown) => {
      answerVerify({ step: "completeAccount", passwordRequired, nameRequired })
      renderFlow()

      await typeCode()

      expect(
        await screen.findByRole("heading", { name: "Complete your account" })
      ).toBeInTheDocument()
      const fields = ["first name", "last name", "password"].filter((label) =>
        screen.queryByLabelText(new RegExp(`^${label}$`, "i"))
      )
      expect(fields).toEqual(shown)
      expect(mocks.completeRegistration).not.toHaveBeenCalled()
    }
  )

  it("creates the account at once when nothing is asked", async () => {
    answerVerify({ step: "completeAccount", passwordRequired: false, nameRequired: false })
    mocks.completeRegistration.mockResolvedValue({
      status: "authenticated",
      requiresPasswordChange: false,
    })
    renderFlow()

    await typeCode()

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    const input = mocks.completeRegistration.mock.calls[0][0]
    expect(input).toMatchObject({ pendingId: "handle-1", otp: "123456" })
    expect(input).not.toHaveProperty("password")
    expect(input).not.toHaveProperty("firstName")
    expect(screen.queryByRole("heading", { name: "Complete your account" })).toBeNull()
  })

  it("sends only the names when only the names are asked", async () => {
    answerVerify({ step: "completeAccount", passwordRequired: false, nameRequired: true })
    mocks.completeRegistration.mockResolvedValue({
      status: "authenticated",
      requiresPasswordChange: false,
    })
    renderFlow()
    await typeCode()
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText(/^first name$/i), "Jane")
    await user.type(screen.getByLabelText(/^last name$/i), "Doe")
    await user.click(screen.getByRole("button", { name: /create account/i }))

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    expect(mocks.completeRegistration.mock.calls[0][0]).toMatchObject({
      firstName: "Jane",
      lastName: "Doe",
      password: undefined,
    })
  })

  it("brings back an account waiting for deletion, with its authenticator app when it has one", async () => {
    answerVerify({ step: "recover" })
    mocks.recoverWithEmailCode
      .mockRejectedValueOnce({
        status: 403,
        code: "User.TwoFactorRequired",
        detail: "Enter your code",
      })
      .mockResolvedValueOnce({ status: "authenticated", requiresPasswordChange: false })
    renderFlow({ requireFullProof: true })
    await typeCode()
    const user = userEvent.setup()

    const restore = await screen.findByRole("button", { name: /restore my account/i })
    await user.click(restore)
    await user.type(await screen.findByLabelText(/^two-factor code$/i), "654321")
    await user.click(restore)

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    expect(mocks.recoverWithEmailCode).toHaveBeenLastCalledWith({
      pendingId: "handle-1",
      otp: "123456",
      twoFactorCode: "654321",
      requireFullProof: true,
    })
  })

  it("closes the code for good after the last wrong try", async () => {
    answerVerify(() => problem(400, "EmailVerification.TooManyAttempts"))
    renderFlow()

    await typeCode()

    expect(
      await screen.findByText("refused: EmailVerification.TooManyAttempts")
    ).toBeInTheDocument()
    await userEvent.setup().type(screen.getByLabelText(/verification code/i), "654321")
    expect(screen.getByRole("button", { name: /^verify$/i })).toBeDisabled()
    expect(mocks.signInWithEmailCode).not.toHaveBeenCalled()
  })
})
