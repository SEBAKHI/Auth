import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { Schemas } from "@authsystem/api/types"

/**
 * Profile › Security › Passkeys (S20 PR C, IN 7 and IN 12): shown only while
 * passkeys run here; add in two clicks, the ceremony started inside the
 * confirm (T-C2); rename in place; remove behind a confirmation held until the
 * request settles; and after every fresh read of the list, the browser told
 * which passkeys the account still accepts, on every device but Apple's.
 */

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  patch: vi.fn(),
  del: vi.fn(),
  loadWebAuthn: vi.fn(),
  startRegistration: vi.fn(),
  signal: vi.fn(),
  logout: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({
  api: { GET: mocks.get, POST: mocks.post, PATCH: mocks.patch, DELETE: mocks.del },
}))
vi.mock("@authsystem/auth/passkey/webauthn", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/auth/passkey/webauthn")>()),
  loadWebAuthn: mocks.loadWebAuthn,
}))
vi.mock("@authsystem/auth/auth-context", () => ({ useAuth: () => ({ logout: mocks.logout }) }))
vi.mock("sonner", () => ({ toast: { success: mocks.toastSuccess, error: mocks.toastError } }))

import { FULL_PROOF_KEY } from "@authsystem/auth/sign-in-method"

import { PASSKEYS_QUERY_KEY, usePasskeys } from "./passkey-list"
import { PasskeysCard } from "./passkeys-card"

type Me = Schemas["UserDto"]
type Passkey = Schemas["PasskeyDto"]

const ME = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "admin@example.com",
  qualifyingMfa: true,
  totpEnabled: true,
  emailCodeAvailable: true,
} as Me

const LAPTOP: Passkey = {
  id: "aaaaaaaa-0000-0000-0000-000000000001",
  name: "Work laptop",
  createdAt: "2026-10-01T09:00:00Z",
  lastUsedAt: "2026-11-01T09:00:00Z",
  isBackupEligible: true,
  isBackedUp: true,
  lastCounterRegressionAt: null,
  credentialId: "Y3JlZC1sYXB0b3A",
}

const KEY: Passkey = {
  id: "aaaaaaaa-0000-0000-0000-000000000002",
  name: "مفتاح الأمان",
  createdAt: "2026-10-02T09:00:00Z",
  lastUsedAt: null,
  isBackupEligible: false,
  isBackedUp: false,
  lastCounterRegressionAt: null,
  credentialId: "Y3JlZC1rZXk",
}

const HANDLE = "dXNlci1oYW5kbGU"

function list(passkeys: Passkey[], userHandle: string | null = HANDLE) {
  return { rpId: "localhost", userHandle, passkeys }
}

function response(status: number) {
  return { status, headers: new Headers() } as unknown as Response
}

function ok<T>(data: T) {
  return { data, error: undefined, response: response(200) }
}

function problem(status: number, code: string) {
  return {
    data: undefined,
    error: { status, code, detail: `server sentence for ${code}` },
    response: response(status),
  }
}

/** The list the server answers, one read after another; the last one repeats. */
function givenLists(...answers: unknown[]) {
  const queue = [...answers]
  mocks.get.mockImplementation(async (path: string) => {
    if (path !== "/api/v1/auth/passkeys") return { data: undefined, error: { status: 404 }, response: response(404) }
    const next = queue.length > 1 ? queue.shift() : queue[0]
    return next
  })
}

const REGISTRATION = {
  options: { challenge: "r1", rp: { id: "localhost", name: "Example" }, user: { id: HANDLE } },
  expiresAt: "2099-01-01T00:00:00Z",
  emailCodeRequired: false,
}

const CREATED = { id: "cred-new", rawId: "cred-new", type: "public-key" }

function Section({ me }: { me: Me }) {
  const passkeys = usePasskeys()
  return <PasskeysCard me={me} passkeys={passkeys} />
}

function renderCard(me: Me = ME, client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  render(
    <QueryClientProvider client={client}>
      <Section me={me} />
    </QueryClientProvider>
  )
  return client
}

function listReads() {
  return mocks.get.mock.calls.filter(([path]) => path === "/api/v1/auth/passkeys").length
}

beforeEach(() => {
  mocks.get.mockReset()
  mocks.post.mockReset()
  mocks.patch.mockReset()
  mocks.del.mockReset()
  mocks.signal.mockReset().mockResolvedValue(undefined)
  mocks.logout.mockReset()
  mocks.toastSuccess.mockReset()
  mocks.toastError.mockReset()
  mocks.startRegistration.mockReset().mockResolvedValue(CREATED)
  mocks.loadWebAuthn.mockReset().mockResolvedValue({ startRegistration: mocks.startRegistration })
  vi.stubGlobal(
    "PublicKeyCredential",
    Object.assign(function PublicKeyCredential() {}, { signalAllAcceptedCredentials: mocks.signal })
  )
  window.sessionStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe("PasskeysCard: when it is shown", () => {
  it("is not there while passkeys do not run: the list answers 404", async () => {
    givenLists(problem(404, "General.NotFound"))
    renderCard()

    await waitFor(() => expect(listReads()).toBe(1))
    expect(screen.queryByTestId("passkeys-card")).not.toBeInTheDocument()
    expect(mocks.signal).not.toHaveBeenCalled()
  })

  it("lists the passkeys: the owner's name in any script, synced, last used, and the counter's way out", async () => {
    givenLists(ok(list([LAPTOP, { ...KEY, lastCounterRegressionAt: "2026-11-02T09:00:00Z" }])))
    renderCard()

    const rows = await screen.findAllByTestId("passkey-row")
    expect(rows).toHaveLength(2)
    // A list of items, as a screen reader announces it (axe: aria-required-children).
    expect(within(screen.getByRole("list")).getAllByRole("listitem")).toEqual(rows)
    expect(within(rows[0]).getByText("Work laptop")).toHaveAttribute("dir", "auto")
    expect(within(rows[0]).getByText(i18n.t("profile.passkeys.synced"))).toBeInTheDocument()
    expect(within(rows[1]).getByText("مفتاح الأمان")).toHaveAttribute("dir", "auto")
    expect(within(rows[1]).queryByText(i18n.t("profile.passkeys.synced"))).not.toBeInTheDocument()
    expect(within(rows[1]).getByText(new RegExp(i18n.t("profile.passkeys.neverUsed")))).toBeInTheDocument()
    expect(within(rows[1]).getByTestId("passkey-counter-regressed")).toHaveTextContent(
      i18n.t("profile.passkeys.counterRegressed")
    )
    expect(within(rows[0]).queryByTestId("passkey-counter-regressed")).not.toBeInTheDocument()
  })

  it("says there are none yet, and offers to add one", async () => {
    givenLists(ok(list([])))
    renderCard()

    expect(await screen.findByText(i18n.t("profile.passkeys.emptyTitle"))).toBeInTheDocument()
    expect(screen.getByRole("button", { name: i18n.t("profile.passkeys.add") })).toBeEnabled()
  })

  it("offers no addition while email is off and the account has no qualifying factor (N9)", async () => {
    givenLists(ok(list([])))
    renderCard({ ...ME, emailCodeAvailable: false, qualifyingMfa: false, totpEnabled: false })

    expect(await screen.findByTestId("passkeys-need-app-first")).toHaveTextContent(
      i18n.t("profile.passkeys.needsAppFirst")
    )
    expect(screen.queryByRole("button", { name: i18n.t("profile.passkeys.add") })).not.toBeInTheDocument()
  })

  it("offers the addition with email off once the account has a qualifying factor", async () => {
    givenLists(ok(list([])))
    renderCard({ ...ME, emailCodeAvailable: false })

    expect(await screen.findByRole("button", { name: i18n.t("profile.passkeys.add") })).toBeEnabled()
  })
})

describe("PasskeysCard: the browser told which passkeys stay (IN 12, D-S20-7)", () => {
  it("is told after the list loads, with the ids, the user handle and the RP ID", async () => {
    givenLists(ok(list([LAPTOP, KEY])))
    renderCard()

    await waitFor(() => expect(mocks.signal).toHaveBeenCalledTimes(1))
    expect(mocks.signal).toHaveBeenCalledWith({
      rpId: "localhost",
      userId: HANDLE,
      allAcceptedCredentialIds: [LAPTOP.credentialId, KEY.credentialId],
    })
  })

  it("is told from a fresh answer only, never from the list already in hand", async () => {
    let answer!: (value: unknown) => void
    mocks.get.mockImplementation(
      () =>
        new Promise((resolve) => {
          answer = resolve
        })
    )
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(PASSKEYS_QUERY_KEY, list([LAPTOP, KEY]))
    renderCard(ME, client)

    // The cached rows show, and the browser is told nothing about them.
    expect(await screen.findAllByTestId("passkey-row")).toHaveLength(2)
    await waitFor(() => expect(listReads()).toBe(1))
    expect(mocks.signal).not.toHaveBeenCalled()

    await act(async () => answer(ok(list([KEY]))))
    await waitFor(() => expect(mocks.signal).toHaveBeenCalledTimes(1))
    expect(mocks.signal).toHaveBeenCalledWith(
      expect.objectContaining({ allAcceptedCredentialIds: [KEY.credentialId] })
    )
  })

  it.each([
    ["an iPhone", "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15"],
    ["an iPad", "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15"],
    ["a Mac", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15"],
  ])("is told nothing on %s (OI-279)", async (_label, userAgent) => {
    vi.spyOn(window.navigator, "userAgent", "get").mockReturnValue(userAgent)
    givenLists(ok(list([LAPTOP])))
    renderCard()

    expect(await screen.findAllByTestId("passkey-row")).toHaveLength(1)
    expect(mocks.signal).not.toHaveBeenCalled()
  })

  it("is told nothing where the client hints say macOS", async () => {
    Object.defineProperty(window.navigator, "userAgentData", {
      value: { platform: "macOS" },
      configurable: true,
    })
    try {
      givenLists(ok(list([LAPTOP])))
      renderCard()

      expect(await screen.findAllByTestId("passkey-row")).toHaveLength(1)
      expect(mocks.signal).not.toHaveBeenCalled()
    } finally {
      delete (window.navigator as { userAgentData?: unknown }).userAgentData
    }
  })

  it("is told nothing for an account without a user handle", async () => {
    givenLists(ok(list([LAPTOP], null)))
    renderCard()

    expect(await screen.findAllByTestId("passkey-row")).toHaveLength(1)
    expect(mocks.signal).not.toHaveBeenCalled()
  })

  it("breaks nothing where the browser lacks the call", async () => {
    vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
    givenLists(ok(list([LAPTOP])))
    renderCard()

    expect(await screen.findAllByTestId("passkey-row")).toHaveLength(1)
  })
})

describe("PasskeysCard: adding a passkey", () => {
  async function openDialog(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.add") }))
    return screen.findByRole("dialog")
  }

  it("asks for the options at the first click, then a titled dialog whose confirm starts the ceremony inside the click", async () => {
    givenLists(ok(list([])), ok(list([LAPTOP])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok(REGISTRATION)
        : ok({ ...LAPTOP })
    )
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/auth/passkeys/registration/options")
    expect(within(dialog).getByRole("heading", { name: i18n.t("profile.passkeys.addTitle") })).toBeInTheDocument()
    expect(dialog).toHaveAccessibleName(i18n.t("profile.passkeys.addTitle"))
    await user.type(within(dialog).getByLabelText(i18n.t("profile.passkeys.name")), "Work laptop")

    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))
    // T-C2: no await between the click and the ceremony.
    expect(mocks.startRegistration).toHaveBeenCalledTimes(1)
    expect(mocks.startRegistration).toHaveBeenCalledWith({ optionsJSON: REGISTRATION.options })

    await waitFor(() =>
      expect(mocks.post).toHaveBeenCalledWith("/api/v1/auth/passkeys/registration/verify", {
        body: { credential: CREATED, name: "Work laptop", emailCode: null },
      })
    )
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument())
    expect(mocks.toastSuccess).toHaveBeenCalledWith(
      i18n.t("profile.passkeys.added", { name: LAPTOP.name })
    )
    // The list is read again, and the browser is told from that fresh read.
    await waitFor(() =>
      expect(mocks.signal).toHaveBeenLastCalledWith(
        expect.objectContaining({ allAcceptedCredentialIds: [LAPTOP.credentialId] })
      )
    )
    expect(listReads()).toBe(2)
  })

  it("says a passkey of the account is already on this device (InvalidStateError)", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(ok(REGISTRATION))
    mocks.startRegistration.mockRejectedValue(Object.assign(new Error("excluded"), { name: "InvalidStateError" }))
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    expect(await within(dialog).findByTestId("add-passkey-message")).toHaveTextContent(
      i18n.t("profile.passkeys.alreadyOnThisDevice")
    )
  })

  it("says nothing when the person cancels the browser's dialog", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(ok(REGISTRATION))
    mocks.startRegistration.mockRejectedValue(Object.assign(new Error("cancelled"), { name: "NotAllowedError" }))
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    await waitFor(() => expect(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") })).toBeEnabled())
    expect(within(dialog).queryByTestId("add-passkey-message")).not.toBeInTheDocument()
  })

  it("shows a name too long on the name field", async () => {
    givenLists(ok(list([])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok(REGISTRATION)
        : problem(400, "Passkey.NameTooLong")
    )
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    expect(await within(dialog).findByText("server sentence for Passkey.NameTooLong")).toBeInTheDocument()
    expect(within(dialog).getByLabelText(i18n.t("profile.passkeys.name"))).toHaveAttribute("aria-invalid", "true")
  })

  it("shows the limit in place when the account holds as many as it may", async () => {
    givenLists(ok(list([LAPTOP])))
    mocks.post.mockResolvedValue(problem(409, "Passkey.LimitReached"))
    const user = userEvent.setup()
    renderCard()

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.add") }))

    expect(await screen.findByTestId("passkeys-message")).toHaveTextContent(
      "server sentence for Passkey.LimitReached"
    )
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
  })

  it("asks for a fresh sign-in that names the passkey, for an account whose factor is one (T-C7)", async () => {
    givenLists(ok(list([LAPTOP])))
    mocks.post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderCard({ ...ME, qualifyingMfa: true, totpEnabled: false })

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.add") }))

    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText(i18n.t("auth.reauthenticatePasskeyBody"))).toBeInTheDocument()
    await user.click(
      within(dialog).getByRole("button", { name: i18n.t("auth.reauthenticateWithPasskeyAction") })
    )
    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    // The sign-in page then offers the passkey first.
    expect(window.sessionStorage.getItem(FULL_PROOF_KEY)).toBe("passkey")
  })

  it("asks for a fresh sign-in in the usual words for an account with the app", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderCard()

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.add") }))

    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText(i18n.t("profile.passkeys.needsRecentSignIn"))).toBeInTheDocument()
  })

  it("starts again when the emailed code finds no registration waiting (N6), never with its sentence", async () => {
    givenLists(ok(list([])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok({ ...REGISTRATION, emailCodeRequired: true })
        : problem(400, "TwoFactor.SetupRequired")
    )
    const user = userEvent.setup()
    renderCard({ ...ME, qualifyingMfa: false, totpEnabled: false })

    const dialog = await openDialog(user)
    expect(within(dialog).getByText(i18n.t("profile.passkeys.emailCodeNeeded"))).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: i18n.t("auth.emailMeACode") }))

    expect(await within(dialog).findByTestId("add-passkey-message")).toHaveTextContent(
      i18n.t("profile.passkeys.startAgain")
    )
    expect(within(dialog).queryByText("server sentence for TwoFactor.SetupRequired")).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.tryAgain") }))
    await waitFor(() =>
      expect(
        mocks.post.mock.calls.filter(([path]) => path === "/api/v1/auth/passkeys/registration/options")
      ).toHaveLength(2)
    )
  })
})

describe("PasskeysCard: renaming and removing", () => {
  it("renames in place, and reads the list again", async () => {
    givenLists(ok(list([LAPTOP])), ok(list([{ ...LAPTOP, name: "Home laptop" }])))
    mocks.patch.mockResolvedValue({ error: undefined, response: response(204) })
    const user = userEvent.setup()
    renderCard()

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.renameNamed", { name: "Work laptop" }) }))
    const field = screen.getByLabelText(i18n.t("profile.passkeys.name"))
    await user.clear(field)
    await user.type(field, "Home laptop")
    await user.click(screen.getByRole("button", { name: i18n.t("common.save") }))

    expect(mocks.patch).toHaveBeenCalledWith("/api/v1/auth/passkeys/{id}", {
      params: { path: { id: LAPTOP.id } },
      body: { name: "Home laptop" },
    })
    expect(await screen.findByText("Home laptop")).toBeInTheDocument()
  })

  it("removes behind a confirmation that names the passkey and the other devices, held until the request settles", async () => {
    givenLists(ok(list([LAPTOP, KEY])), ok(list([KEY])))
    let settle!: (value: unknown) => void
    mocks.del.mockImplementation(
      () =>
        new Promise((resolve) => {
          settle = resolve
        })
    )
    const user = userEvent.setup()
    renderCard()

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.removeNamed", { name: "Work laptop" }) }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText(i18n.t("profile.passkeys.removeTitle", { name: "Work laptop" }))).toBeInTheDocument()
    expect(within(dialog).getByText(i18n.t("profile.passkeys.removeBody"))).toBeInTheDocument()

    const remove = within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.remove") })
    // A plain button: the dialog's own action would close it before the answer.
    expect(remove).toHaveAttribute("data-slot", "button")
    await user.click(remove)

    expect(mocks.del).toHaveBeenCalledWith("/api/v1/auth/passkeys/{id}", {
      params: { path: { id: LAPTOP.id } },
    })
    expect(screen.getByRole("alertdialog")).toBeInTheDocument()
    expect(
      within(screen.getByRole("alertdialog")).getByRole("button", { name: new RegExp(i18n.t("profile.passkeys.remove")) })
    ).toBeDisabled()

    await act(async () => settle({ error: undefined, response: response(204) }))

    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
    expect(mocks.toastSuccess).toHaveBeenCalledWith(
      i18n.t("profile.passkeys.removed", { name: "Work laptop" })
    )
    await waitFor(() =>
      expect(mocks.signal).toHaveBeenLastCalledWith(
        expect.objectContaining({ allAcceptedCredentialIds: [KEY.credentialId] })
      )
    )
    await waitFor(() =>
      expect(screen.getByRole("button", { name: i18n.t("profile.passkeys.add") })).toHaveFocus()
    )
  })

  it("shows a refused removal on the card, and keeps the passkey", async () => {
    givenLists(ok(list([LAPTOP])))
    mocks.del.mockResolvedValue(problem(429, "General.RateLimited"))
    const user = userEvent.setup()
    renderCard()

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.removeNamed", { name: "Work laptop" }) }))
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: i18n.t("profile.passkeys.remove") }))

    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
    expect(screen.getByTestId("passkeys-message")).toHaveTextContent(i18n.t("auth.passkey.rateLimited"))
    expect(screen.getAllByTestId("passkey-row")).toHaveLength(1)
  })

  it("asks for a fresh sign-in when the removal needs one", async () => {
    givenLists(ok(list([LAPTOP])))
    mocks.del.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderCard({ ...ME, qualifyingMfa: true, totpEnabled: false })

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.removeNamed", { name: "Work laptop" }) }))
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: i18n.t("profile.passkeys.remove") }))

    expect(await screen.findByText(i18n.t("auth.reauthenticatePasskeyBody"))).toBeInTheDocument()
  })
})
