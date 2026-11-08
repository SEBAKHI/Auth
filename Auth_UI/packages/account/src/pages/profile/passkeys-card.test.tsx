import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react"
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
  cancelCeremony: vi.fn(),
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
  mocks.cancelCeremony.mockReset()
  mocks.loadWebAuthn.mockReset().mockResolvedValue({
    startRegistration: mocks.startRegistration,
    WebAuthnAbortService: { cancelCeremony: mocks.cancelCeremony },
  })
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

  it("is not there when a first read fails and the account holds none: passkeys may not run here", async () => {
    // The gateway answers before the API's 404, so a 429 or a 503 says nothing of
    // whether passkeys run: a deployment without them must not show the card.
    givenLists(problem(503, "General.Unavailable"))
    const client = renderCard({ ...ME, passkeyCount: 0 })

    await waitFor(() => expect(client.getQueryState(PASSKEYS_QUERY_KEY)?.status).toBe("error"))
    expect(screen.queryByTestId("passkeys-card")).not.toBeInTheDocument()
  })

  it("says a failed first read, with a retry, for an account known to hold a passkey", async () => {
    givenLists(problem(503, "General.Unavailable"), ok(list([LAPTOP])))
    const user = userEvent.setup()
    renderCard({ ...ME, passkeyCount: 1 })

    await user.click(await screen.findByRole("button", { name: i18n.t("common.retry") }))

    expect(await screen.findAllByTestId("passkey-row")).toHaveLength(1)
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

  it("is told nothing by a read that a newer read replaced, even when it answers last", async () => {
    const answers: Array<(value: unknown) => void> = []
    mocks.get.mockImplementation(() => new Promise((resolve) => answers.push(resolve)))
    const client = renderCard()
    await waitFor(() => expect(listReads()).toBe(1))
    await act(async () => answers[0](ok(list([LAPTOP]))))
    await waitFor(() => expect(mocks.signal).toHaveBeenCalledTimes(1))

    // A change to the account reads the list again; the addition's own read replaces that one.
    act(() => void client.invalidateQueries({ queryKey: PASSKEYS_QUERY_KEY }))
    await waitFor(() => expect(listReads()).toBe(2))
    act(() => void client.invalidateQueries({ queryKey: PASSKEYS_QUERY_KEY }))
    await waitFor(() => expect(listReads()).toBe(3))

    await act(async () => answers[2](ok(list([LAPTOP, KEY]))))
    await waitFor(() => expect(mocks.signal).toHaveBeenCalledTimes(2))
    // The older read, without the passkey just added, answers last.
    await act(async () => answers[1](ok(list([LAPTOP]))))
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(mocks.signal).toHaveBeenCalledTimes(2)
    expect(mocks.signal).toHaveBeenLastCalledWith(
      expect.objectContaining({ allAcceptedCredentialIds: [LAPTOP.credentialId, KEY.credentialId] })
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
    // The server checks the name after spending the challenge: only fresh options
    // can go on, and no second ceremony runs on these.
    expect(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.tryAgain") })).toBeInTheDocument()
    expect(within(dialog).queryByRole("button", { name: i18n.t("profile.passkeys.create") })).not.toBeInTheDocument()
  })

  it("refuses a name over the limit before the device is asked: the limit is on code points", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(ok(REGISTRATION))
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    const field = within(dialog).getByLabelText(i18n.t("profile.passkeys.name"))
    const create = within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") })

    fireEvent.change(field, { target: { value: "😀".repeat(65) } })
    fireEvent.click(create)

    expect(mocks.startRegistration).not.toHaveBeenCalled()
    expect(
      within(dialog).getByText(i18n.t("profile.passkeys.nameTooLong", { max: 64 }))
    ).toBeInTheDocument()

    // Sixty-four of them are 128 UTF-16 units, and fit.
    fireEvent.change(field, { target: { value: "😀".repeat(64) } })
    fireEvent.click(create)

    expect(mocks.startRegistration).toHaveBeenCalledTimes(1)
  })

  it("the limit reached at the verify offers only fresh options", async () => {
    givenLists(ok(list([])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok(REGISTRATION)
        : problem(409, "Passkey.LimitReached")
    )
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    expect(await within(dialog).findByTestId("add-passkey-message")).toHaveTextContent(
      "server sentence for Passkey.LimitReached"
    )
    expect(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.tryAgain") })).toBeInTheDocument()
  })

  it("a refused ceremony gives no sign-in advice to a person who is signed in, and starts again with fresh options", async () => {
    givenLists(ok(list([])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok(REGISTRATION)
        : problem(400, "Passkey.VerificationFailed")
    )
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    const message = await within(dialog).findByTestId("add-passkey-message")
    expect(message).toHaveTextContent("server sentence for Passkey.VerificationFailed")
    expect(message).toHaveTextContent(i18n.t("profile.passkeys.startAgain"))
    expect(message).not.toHaveTextContent(i18n.t("auth.passkey.removedPasskeyWayOut"))

    await user.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.tryAgain") }))
    await waitFor(() =>
      expect(
        mocks.post.mock.calls.filter(([path]) => path === "/api/v1/auth/passkeys/registration/options")
      ).toHaveLength(2)
    )
  })

  it("a device clock ahead of the server's can still add a passkey: the server decides, not that clock", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(
      ok({ ...REGISTRATION, expiresAt: new Date(Date.now() - 60_000).toISOString() })
    )
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    expect(mocks.startRegistration).toHaveBeenCalledTimes(1)
  })

  it("asks for fresh options once these have outlived their five minutes on this page's own clock", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(ok(REGISTRATION))
    const user = userEvent.setup()
    renderCard()

    const dialog = await openDialog(user)
    const start = performance.now()
    vi.spyOn(performance, "now").mockReturnValue(start + 5 * 60_000)
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))

    expect(mocks.startRegistration).not.toHaveBeenCalled()
    await waitFor(() =>
      expect(
        mocks.post.mock.calls.filter(([path]) => path === "/api/v1/auth/passkeys/registration/options")
      ).toHaveLength(2)
    )
  })

  function emailCodePosts() {
    return mocks.post.mock.calls.filter(([path]) => path === "/api/v1/auth/passkeys/registration/email-code").length
  }

  it("offers a new code once the wait is over, and asks for it", async () => {
    givenLists(ok(list([])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok({ ...REGISTRATION, emailCodeRequired: true })
        : ok({ emailCodeRequired: true, sentTo: "a***@example.com", expiresAt: "2099-01-01T00:00:00Z" })
    )
    const user = userEvent.setup()
    renderCard({ ...ME, qualifyingMfa: false, totpEnabled: false })

    const dialog = await openDialog(user)
    await user.click(within(dialog).getByRole("button", { name: i18n.t("auth.emailMeACode") }))
    await waitFor(() => expect(emailCodePosts()).toBe(1))
    expect(
      await within(dialog).findByRole("button", { name: i18n.t("accountDeletion.resendIn", { seconds: 60 }) })
    ).toBeDisabled()

    const realNow = Date.now.bind(Date)
    vi.spyOn(Date, "now").mockImplementation(() => realNow() + 61_000)
    await user.click(await within(dialog).findByRole("button", { name: i18n.t("accountDeletion.resendCode") }))

    await waitFor(() => expect(emailCodePosts()).toBe(2))
  })

  it("asks for fresh options when the server says no code is needed any more", async () => {
    givenLists(ok(list([])))
    mocks.post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/passkeys/registration/options"
        ? ok({ ...REGISTRATION, emailCodeRequired: true })
        : ok({ emailCodeRequired: false, sentTo: null, expiresAt: null })
    )
    const user = userEvent.setup()
    renderCard({ ...ME, qualifyingMfa: false, totpEnabled: false })

    const dialog = await openDialog(user)
    await user.click(within(dialog).getByRole("button", { name: i18n.t("auth.emailMeACode") }))

    await waitFor(() =>
      expect(
        mocks.post.mock.calls.filter(([path]) => path === "/api/v1/auth/passkeys/registration/options")
      ).toHaveLength(2)
    )
    expect(screen.queryByText(i18n.t("auth.emailCodeSentTo", { email: "" }))).not.toBeInTheDocument()
  })

  it("leaves no ceremony behind when the page goes", async () => {
    givenLists(ok(list([])))
    mocks.post.mockResolvedValue(ok(REGISTRATION))
    const user = userEvent.setup()
    renderCard()

    await openDialog(user)
    expect(mocks.cancelCeremony).not.toHaveBeenCalled()
    cleanup()

    expect(mocks.cancelCeremony).toHaveBeenCalled()
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
    // The form is gone: the focus goes back to the button that opened it.
    expect(screen.getByRole("button", { name: i18n.t("profile.passkeys.renameNamed", { name: "Home laptop" }) })).toHaveFocus()
  })

  it("returns the focus to the rename button when Escape closes the form", async () => {
    givenLists(ok(list([LAPTOP])))
    const user = userEvent.setup()
    renderCard()

    const rename = await screen.findByRole("button", { name: i18n.t("profile.passkeys.renameNamed", { name: "Work laptop" }) })
    await user.click(rename)
    await user.keyboard("{Escape}")

    expect(screen.getByRole("button", { name: i18n.t("profile.passkeys.renameNamed", { name: "Work laptop" }) })).toHaveFocus()
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

  it("asks a passkey-only account removing a lost passkey for a plain recent sign-in, not for the passkey it lost", async () => {
    // Adding beside a passkey needs a two-factor session (PK7); removing asks only
    // for a recent first factor, or a person who lost the passkey could never
    // remove it: their password counts here.
    givenLists(ok(list([LAPTOP])))
    mocks.del.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderCard({ ...ME, qualifyingMfa: true, totpEnabled: false })

    await user.click(await screen.findByRole("button", { name: i18n.t("profile.passkeys.removeNamed", { name: "Work laptop" }) }))
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: i18n.t("profile.passkeys.remove") }))

    const dialog = await screen.findByText(i18n.t("profile.passkeys.needsRecentSignIn"))
    expect(dialog).toBeInTheDocument()
    expect(screen.queryByText(i18n.t("auth.reauthenticatePasskeyBody"))).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: i18n.t("auth.reauthenticateAction") }))
    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    expect(window.sessionStorage.getItem(FULL_PROOF_KEY)).toBe("1")
  })
})
