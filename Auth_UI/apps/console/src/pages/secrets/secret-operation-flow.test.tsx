import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"


const post = vi.fn()
const toastSuccess = vi.fn()
const toastError = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ user: { email: "admin@company.com" } }),
}))

// input-otp schedules pointer hit-testing that jsdom does not implement. This
// adapter preserves the flow's value/onComplete contract without browser-only
// geometry, which is covered by the shared OtpInput component's own tests.
vi.mock("@authsystem/ui/common/otp-input", () => ({
  OTP_CODE_LENGTH: 6,
  RESEND_COOLDOWN_MS: 60_000,
  OtpInput: ({
    value,
    onChange,
    onComplete,
    label,
    disabled,
  }: {
    value: string
    onChange: (value: string) => void
    onComplete?: (value: string) => void
    label: string
    disabled?: boolean
  }) => (
    <input
      aria-label={label}
      disabled={disabled}
      value={value}
      onChange={(event) => {
        const next = event.target.value
        onChange(next)
        if (next.length === 6) onComplete?.(next)
      }}
    />
  ),
}))

vi.mock("sonner", () => ({
  toast: {
    success: (...args: unknown[]) => toastSuccess(...args),
    error: (...args: unknown[]) => toastError(...args),
  },
}))

import {
  type PendingSecretOperation,
  SecretOperationFlow,
  type SecretOperationName,
  type SecretOperationResult,
} from "./secret-operation-flow"

const challenge = {
  challengeId: "challenge-1",
  maskedEmail: "a***@company.com",
  expiresAt: "2099-01-01T00:00:00Z",
}

const impact = {
  affectedUsers: "2",
  approvalExpiresAt: "2099-01-01T00:00:00Z",
  requiresApiRestart: true,
  requiresGatewayReconfiguration: true,
  details: [
    { code: "usersSignedOut", count: "2" },
    { code: "futureImpactCode", count: 1 },
  ],
}

function renderFlow(
  pending: PendingSecretOperation,
  onClose = vi.fn(),
  onExecuted = vi.fn()
) {
  const client = new QueryClient({
    defaultOptions: { mutations: { retry: false }, queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <SecretOperationFlow
        pending={pending}
        onClose={onClose}
        onExecuted={onExecuted}
      />
    </QueryClientProvider>
  )
  return { onClose, onExecuted }
}

async function completeApproval() {
  const user = userEvent.setup()
  await user.click(screen.getByRole("button", { name: "Continue" }))
  await user.type(await screen.findByLabelText("Confirmation code"), "123456")
  await screen.findByText("Last chance to stop")
  await user.type(
    screen.getByLabelText("Type admin@company.com to confirm."),
    " ADMIN@COMPANY.COM "
  )
  await user.click(screen.getByRole("button", { name: "I understand — do it" }))
}

type OperationCase = {
  operation: SecretOperationName
  value?: string
  endpoint: string
  response?: unknown
  expected: SecretOperationResult
}

const operationCases: OperationCase[] = [
  {
    operation: "GenerateRsaKey",
    endpoint: "/api/v1/admin/Secrets/generate/rsa",
    response: { publicKeyPem: "PUBLIC" },
    expected: { value: "PUBLIC", multiline: true },
  },
  {
    operation: "GenerateHmacKey",
    endpoint: "/api/v1/admin/Secrets/generate/hmac",
    expected: { multiline: false },
  },
  {
    operation: "GenerateGatewayToken",
    endpoint: "/api/v1/admin/Secrets/generate/gateway-token",
    response: { token: "gateway-token" },
    expected: { value: "gateway-token", multiline: false },
  },
  {
    operation: "ImportRsaKey",
    value: "PRIVATE",
    endpoint: "/api/v1/admin/Secrets/import/rsa",
    response: { publicKeyPem: "DERIVED PUBLIC" },
    expected: { value: "DERIVED PUBLIC", multiline: true },
  },
  {
    operation: "ImportHmacKey",
    value: "aG1hYw==",
    endpoint: "/api/v1/admin/Secrets/import/hmac",
    expected: { multiline: false },
  },
  {
    operation: "ImportGatewayToken",
    value: "imported-gateway-token",
    endpoint: "/api/v1/admin/Secrets/import/gateway-token",
    expected: { multiline: false },
  },
]

describe("SecretOperationFlow", () => {
  beforeEach(() => {
    post.mockReset().mockImplementation((path: string) => {
      if (path === "/api/v1/admin/Secrets/challenges") {
        return Promise.resolve({ data: challenge })
      }
      if (path.includes("/verify")) return Promise.resolve({ data: impact })
      const operation = operationCases.find(
        (candidate) => candidate.endpoint === path
      )
      return Promise.resolve({ data: operation?.response })
    })
    toastSuccess.mockReset()
    toastError.mockReset()
  })

  it.each(operationCases)(
    "executes $operation only after challenge verification and typed impact confirmation",
    async ({ operation, value, endpoint, expected }) => {
      const callbacks = renderFlow({ operation, value })

      await completeApproval()

      await waitFor(() =>
        expect(callbacks.onExecuted).toHaveBeenCalledWith(expected)
      )
      expect(callbacks.onClose).toHaveBeenCalledOnce()
      expect(post).toHaveBeenCalledWith("/api/v1/admin/Secrets/challenges", {
        body: { operation, value },
      })
      expect(post).toHaveBeenCalledWith(
        "/api/v1/admin/Secrets/challenges/{challengeId}/verify",
        {
          params: { path: { challengeId: "challenge-1" } },
          body: { code: "123456" },
        }
      )
      expect(post).toHaveBeenCalledWith(endpoint, {
        body:
          value === undefined
            ? { challengeId: "challenge-1" }
            : { value, challengeId: "challenge-1" },
      })
      expect(screen.getByText("2 users will be affected")).toBeInTheDocument()
      expect(screen.getByText(/futureImpactCode/)).toBeInTheDocument()
    },
    10_000
  )

  it("keeps a rejected code recoverable and does not advance to impact", async () => {
    post.mockImplementation((path: string) => {
      if (path === "/api/v1/admin/Secrets/challenges") {
        return Promise.resolve({ data: challenge })
      }
      return Promise.resolve({
        error: {
          status: 400,
          code: "Secret.InvalidChallengeCode",
          // The sentence the DomainErrors catalog holds for this code, in the
          // seven languages a backend test keeps it complete in.
          detail:
            "The confirmation code is incorrect or is no longer valid. Request a new code and try again.",
        },
      })
    })
    renderFlow({ operation: "GenerateHmacKey" })
    const user = userEvent.setup()

    await user.click(screen.getByRole("button", { name: "Continue" }))
    const codeInput = await screen.findByLabelText("Confirmation code")
    await user.type(codeInput, "123456")

    await screen.findByText(
      "The confirmation code is incorrect or is no longer valid. Request a new code and try again."
    )
    expect(codeInput).toHaveValue("")
    expect(screen.queryByText("Last chance to stop")).not.toBeInTheDocument()
  })

  it("closes the spent approval when execution fails", async () => {
    post.mockImplementation((path: string) => {
      if (path === "/api/v1/admin/Secrets/challenges") {
        return Promise.resolve({ data: challenge })
      }
      if (path.includes("/verify")) return Promise.resolve({ data: impact })
      return Promise.resolve({ error: { detail: "Rotation failed." } })
    })
    const callbacks = renderFlow({ operation: "GenerateHmacKey" })

    await completeApproval()

    await waitFor(() => expect(callbacks.onClose).toHaveBeenCalledOnce())
    expect(callbacks.onExecuted).not.toHaveBeenCalled()
    expect(toastError).toHaveBeenCalled()
  })
})

type KeyringCase = {
  pending: PendingSecretOperation
  endpoint: string
  request: Record<string, unknown>
}

/**
 * S11: a keyring change runs the same three gates, bound to its action, and is
 * executed against the keyring endpoints. Never against generate/hmac or
 * import/hmac, which are the emergency aliases kept for consoles cached before
 * the keyring: a scheduled rotation sent there would sign everyone out.
 */
const keyringCases: KeyringCase[] = [
  {
    pending: { operation: "GenerateHmacKey", keyringAction: "Stage" },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/stage",
    request: { body: { challengeId: "challenge-1", value: null } },
  },
  {
    pending: { operation: "ImportHmacKey", keyringAction: "StageImport", value: "aG1hYw==" },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/stage",
    request: { body: { challengeId: "challenge-1", value: "aG1hYw==" } },
  },
  {
    pending: { operation: "GenerateHmacKey", keyringAction: "Promote", keyId: 2 },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/promote",
    request: { body: { challengeId: "challenge-1", emergency: false } },
  },
  {
    pending: { operation: "GenerateHmacKey", keyringAction: "PromoteEmergency" },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/promote",
    request: { body: { challengeId: "challenge-1", emergency: true } },
  },
  {
    pending: { operation: "GenerateHmacKey", keyringAction: "DiscardNext", keyId: 2 },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/discard-next",
    request: { body: { challengeId: "challenge-1" } },
  },
  {
    pending: { operation: "GenerateHmacKey", keyringAction: "Reinstate", keyId: 1 },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/reinstate/{id}",
    request: { params: { path: { id: 1 } }, body: { challengeId: "challenge-1" } },
  },
  {
    pending: { operation: "GenerateHmacKey", keyringAction: "Retire", keyId: 1 },
    endpoint: "/api/v1/admin/Secrets/keyring/hmac/retire/{id}",
    request: { params: { path: { id: 1 } }, body: { challengeId: "challenge-1" } },
  },
]

describe("SecretOperationFlow — keyring changes", () => {
  beforeEach(() => {
    post.mockReset().mockImplementation((path: string) => {
      if (path === "/api/v1/admin/Secrets/challenges") {
        return Promise.resolve({ data: challenge })
      }
      if (path.includes("/verify")) {
        return Promise.resolve({
          data: { ...impact, requiresApiRestart: false, requiresGatewayReconfiguration: false },
        })
      }
      return Promise.resolve({ data: {} })
    })
    toastSuccess.mockReset()
    toastError.mockReset()
  })

  it.each(keyringCases)(
    "binds $pending.keyringAction to its action and runs it on its keyring endpoint",
    async ({ pending, endpoint, request }) => {
      const callbacks = renderFlow(pending)

      await completeApproval()

      await waitFor(() => expect(callbacks.onExecuted).toHaveBeenCalled())
      const namesItsKey =
        pending.keyringAction === "Reinstate" || pending.keyringAction === "Retire"
      expect(post).toHaveBeenCalledWith("/api/v1/admin/Secrets/challenges", {
        body: {
          operation: pending.operation,
          value: pending.value,
          keyringAction: pending.keyringAction,
          keyId: namesItsKey ? pending.keyId : undefined,
        },
      })
      expect(post).toHaveBeenCalledWith(
        "/api/v1/admin/Secrets/challenges/{challengeId}/verify",
        {
          params: { path: { challengeId: "challenge-1" } },
          body: {
            code: "123456",
            keyringAction: pending.keyringAction,
            keyId: namesItsKey ? pending.keyId : undefined,
            value: pending.keyringAction === "StageImport" ? pending.value : undefined,
          },
        }
      )
      expect(post).toHaveBeenCalledWith(endpoint, request)
      const paths = post.mock.calls.map(([path]) => path as string)
      expect(paths).toHaveLength(3)
      expect(paths).not.toContain("/api/v1/admin/Secrets/generate/hmac")
      expect(paths).not.toContain("/api/v1/admin/Secrets/import/hmac")
      expect(toastSuccess).toHaveBeenCalledWith(
        "Keyring updated. Back up the secrets file now."
      )
    },
    10_000
  )

  it("names the action and its key, and states the action's own cost, before a code is sent", async () => {
    renderFlow({ operation: "GenerateHmacKey", keyringAction: "Retire", keyId: 3 })

    expect(await screen.findByText("Retire HMAC key 3")).toBeInTheDocument()
    expect(
      screen.getByText(/removed from the secrets file and from its backup copy for good/)
    ).toBeInTheDocument()
    expect(
      screen.queryByText(/Regenerating signing keys invalidates all existing tokens/)
    ).not.toBeInTheDocument()
  })
})
