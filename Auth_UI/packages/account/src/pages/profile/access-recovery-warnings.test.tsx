import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"

/**
 * The security tab's two warnings to an administrator (owner, 2026-11-24): signing
 * in on one device only, and nobody to restore access. Deliberate breaks, verified
 * red: a synced passkey warned of as one device; the warning kept for a second
 * passkey; an account that administers nothing (null) warned as having nobody; the
 * status read for an account that administers nothing and has no app. Each absence
 * is read once the other warning shows, so the status is in hand: read on the
 * request alone, the first two breaks stayed green.
 */
vi.mock("@authsystem/api/client", () => ({
  api: { GET: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

import { api } from "@authsystem/api/client"

import { AccessRecoveryWarnings } from "./access-recovery-warnings"

const get = api.GET as unknown as ReturnType<typeof vi.fn>

const ADMIN = {
  id: "admin-1",
  email: "admin@example.com",
  totpEnabled: false,
  twoFactorEnabled: true,
  permissions: ["users:read"],
} as Schemas["UserDto"]

type Status = Schemas["TwoFactorStatusResponse"]
type PasskeyList = Schemas["PasskeyListDto"]

function passkey(id: string, isBackedUp: boolean, isBackupEligible = isBackedUp) {
  return { id, credentialId: id, name: id, createdAt: "2026-11-01T10:00:00Z", isBackedUp, isBackupEligible }
}

function list(...passkeys: ReturnType<typeof passkey>[]): PasskeyList {
  return { rpId: "example.com", userHandle: "u", passkeys } as unknown as PasskeyList
}

/** The status as the API sends it: a member given as undefined is left out, as the API leaves out a null. */
function givenStatus(status: Partial<Status>) {
  const data: Record<string, unknown> = {
    recoveryCodesRemaining: null,
    signsInWithPasskeyOnly: false,
    anotherAdministratorCanReset: true,
    ...status,
  }
  for (const [key, value] of Object.entries(status)) if (value === undefined) delete data[key]
  get.mockResolvedValue({ data })
}

function renderWarnings(me: Schemas["UserDto"], passkeys: PasskeyList | null) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <AccessRecoveryWarnings me={me} passkeys={passkeys} />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  get.mockReset()
})

describe("one device only", () => {
  // REVIEW 185 F7. Deliberate break, verified red: the rule reading `isBackupEligible`.
  it("warns when the one passkey could be synced but is not (BE=1, BS=0)", async () => {
    givenStatus({ signsInWithPasskeyOnly: true })
    renderWarnings(ADMIN, list(passkey("phone", false, true)))

    expect(await screen.findByTestId("passkey-one-device-warning")).toBeInTheDocument()
  })

  it("warns a passkey-only administrator whose one passkey is not synced", async () => {
    givenStatus({ signsInWithPasskeyOnly: true })
    renderWarnings(ADMIN, list(passkey("work-pc", false)))

    expect(await screen.findByTestId("passkey-one-device-warning")).toHaveTextContent(
      "profile.passkeyOneDeviceTitle"
    )
  })

  it.each([
    ["the one passkey is synced", list(passkey("phone", true))],
    ["there is a second passkey", list(passkey("work-pc", false), passkey("key", false))],
    ["passkeys do not run here", null],
  ])("says nothing when %s", async (_, passkeys) => {
    // The other warning shows: the status is in hand when the absence is read.
    givenStatus({ signsInWithPasskeyOnly: true, anotherAdministratorCanReset: false })
    renderWarnings(ADMIN, passkeys)

    await screen.findByTestId("sole-administrator-warning")
    expect(screen.queryByTestId("passkey-one-device-warning")).toBeNull()
  })

  it("says nothing to an account that signs in other ways too", async () => {
    givenStatus({ signsInWithPasskeyOnly: false, anotherAdministratorCanReset: false })
    renderWarnings(ADMIN, list(passkey("work-pc", false)))

    await screen.findByTestId("sole-administrator-warning")
    expect(screen.queryByTestId("passkey-one-device-warning")).toBeNull()
  })
})

describe("nobody to restore access", () => {
  it("warns an administrator no other administrator could reset", async () => {
    givenStatus({ anotherAdministratorCanReset: false })
    renderWarnings(ADMIN, null)

    expect(await screen.findByTestId("sole-administrator-warning")).toHaveTextContent(
      "profile.soleAdministratorTitle"
    )
  })

  it.each([
    ["another administrator can", true],
    ["the account administers nothing (null)", null],
    // REVIEW 185 F7: the API leaves a null out, so the page meets an absent member.
    ["the account administers nothing (left out, as the API sends it)", undefined],
  ])("says nothing when %s", async (_, anotherAdministratorCanReset) => {
    // The other warning shows: the status is in hand when the absence is read.
    givenStatus({ anotherAdministratorCanReset, signsInWithPasskeyOnly: true })
    renderWarnings(ADMIN, list(passkey("work-pc", false)))

    await screen.findByTestId("passkey-one-device-warning")
    expect(screen.queryByTestId("sole-administrator-warning")).toBeNull()
  })
})

it("reads nothing for an account that administers nothing and has no app", () => {
  renderWarnings({ ...ADMIN, permissions: [], twoFactorEnabled: false }, null)

  expect(get).not.toHaveBeenCalled()
})
