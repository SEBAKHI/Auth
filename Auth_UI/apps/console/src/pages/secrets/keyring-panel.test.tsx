import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const get = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => get(...args) },
}))

import { KeyringPanel } from "./keyring-panel"

type Entry = {
  id: number
  state: "Current" | "Next" | "Previous"
  createdAtUtc: string | null
  demotedAtUtc: string | null
  verifyUntilUtc: string | null
  webhookGraceUntilUtc: string | null
  compromised: boolean
  webhookKeyCount: number
  loadedInProcess: boolean
}

const FUTURE = "2999-01-01T00:00:00Z"
const PAST = "2001-01-01T00:00:00Z"

function current(id = 1): Entry {
  return {
    id,
    state: "Current",
    createdAtUtc: id === 1 ? null : "2026-10-01T00:00:00Z",
    demotedAtUtc: null,
    verifyUntilUtc: null,
    webhookGraceUntilUtc: null,
    compromised: false,
    webhookKeyCount: 0,
    loadedInProcess: true,
  }
}

function staged(id: number, loadedInProcess = true): Entry {
  return {
    ...current(id),
    state: "Next",
    createdAtUtc: "2026-10-20T00:00:00Z",
    verifyUntilUtc: FUTURE,
    loadedInProcess,
  }
}

function previous(id: number, overrides: Partial<Entry> = {}): Entry {
  return {
    ...current(id),
    state: "Previous",
    demotedAtUtc: "2026-10-20T00:00:00Z",
    verifyUntilUtc: FUTURE,
    ...overrides,
  }
}

function keyring(keys: Entry[], inSync = true) {
  const currentKey = keys.find((key) => key.state === "Current")
  const next = keys.find((key) => key.state === "Next")
  return {
    data: {
      currentKeyId: currentKey?.id ?? 1,
      processCurrentKeyId: inSync ? (currentKey?.id ?? 1) : 1,
      nextKeyId: next?.id ?? null,
      processNextKeyId: next?.loadedInProcess ? next.id : null,
      inSync,
      legacyCutoverUtc: null,
      keys,
    },
  }
}

function renderPanel(busy = false) {
  const onChange = vi.fn()
  const onStageImport = vi.fn()
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  render(
    <QueryClientProvider client={client}>
      <KeyringPanel busy={busy} onChange={onChange} onStageImport={onStageImport} />
    </QueryClientProvider>
  )
  return { onChange, onStageImport }
}

function rowOf(id: number) {
  const cell = screen.getAllByRole("cell").find((c) => c.textContent === String(id))
  if (!cell) throw new Error(`no row for key ${id}`)
  return cell.closest("tr") as HTMLElement
}

describe("KeyringPanel", () => {
  beforeEach(() => {
    get.mockReset()
  })

  it("on a deployment that never rotated, offers a stage and says why promote and discard wait", async () => {
    get.mockResolvedValue(keyring([current(1)]))
    renderPanel()

    expect(await screen.findByText("Unknown (the original key)")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Stage a new key" })).toBeEnabled()
    expect(screen.getByRole("button", { name: "Promote the staged key" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Discard the staged key" })).toBeDisabled()
    // A disabled control always says why, next to it.
    expect(screen.getByText("No key is staged: stage one first.")).toBeInTheDocument()
    // The current key offers no row action, and says what it does instead.
    expect(within(rowOf(1)).getByText("Issues every new fingerprint.")).toBeInTheDocument()
    expect(
      within(rowOf(1)).queryByRole("button", { name: "Actions for key 1" })
    ).not.toBeInTheDocument()
  })

  it("with a key staged but not loaded by this process, keeps promote closed and says to restart", async () => {
    get.mockResolvedValue(keyring([current(1), staged(2, false)]))
    renderPanel()

    expect(
      await screen.findByText(/Key 2 is staged in the secrets file but this process has not loaded it/)
    ).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Promote the staged key" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Discard the staged key" })).toBeEnabled()
    expect(screen.getByRole("button", { name: "Stage a new key" })).toBeDisabled()
    expect(screen.getByText(/Key 2 is already staged: promote it or discard it/)).toBeInTheDocument()
  })

  it("promotes and discards the staged key by its id", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(keyring([current(1), staged(2)]))
    const { onChange } = renderPanel()

    await user.click(await screen.findByRole("button", { name: "Promote the staged key" }))
    expect(onChange).toHaveBeenLastCalledWith({
      operation: "GenerateHmacKey",
      keyringAction: "Promote",
      keyId: 2,
    })

    await user.click(screen.getByRole("button", { name: "Discard the staged key" }))
    expect(onChange).toHaveBeenLastCalledWith({
      operation: "GenerateHmacKey",
      keyringAction: "DiscardNext",
      keyId: 2,
    })
  })

  it("stages a generated key, or opens the import dialog for a supplied one", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(keyring([current(1)]))
    const { onChange, onStageImport } = renderPanel()

    await user.click(await screen.findByRole("button", { name: "Stage a new key" }))
    await user.click(await screen.findByRole("menuitem", { name: "Generate a key" }))
    expect(onChange).toHaveBeenLastCalledWith({
      operation: "GenerateHmacKey",
      keyringAction: "Stage",
    })

    await user.click(screen.getByRole("button", { name: "Stage a new key" }))
    await user.click(await screen.findByRole("menuitem", { name: "Import a key" }))
    expect(onStageImport).toHaveBeenCalledTimes(1)
  })

  it("keeps the emergency rotation open whatever the keyring holds", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(keyring([current(1), staged(2, false)]))
    const { onChange } = renderPanel()

    await user.click(await screen.findByRole("button", { name: "Emergency rotation" }))
    expect(onChange).toHaveBeenLastCalledWith({
      operation: "GenerateHmacKey",
      keyringAction: "PromoteEmergency",
    })
  })

  it("starts nothing while another confirmation is open", async () => {
    get.mockResolvedValue(keyring([current(2), staged(3), previous(1)]))
    renderPanel(true)

    await screen.findByText("Previous")
    for (const name of [
      "Stage a new key",
      "Promote the staged key",
      "Discard the staged key",
      "Emergency rotation",
      "Actions for key 1",
    ]) {
      expect(screen.getByRole("button", { name })).toBeDisabled()
    }
  })

  it("reinstates and retires an older key by its id", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(keyring([current(2), previous(1)]))
    const { onChange } = renderPanel()

    await user.click(await screen.findByRole("button", { name: "Actions for key 1" }))
    await user.click(await screen.findByRole("menuitem", { name: "Reinstate (roll back to this key)" }))
    expect(onChange).toHaveBeenLastCalledWith({
      operation: "GenerateHmacKey",
      keyringAction: "Reinstate",
      keyId: 1,
    })

    await user.click(screen.getByRole("button", { name: "Actions for key 1" }))
    await user.click(await screen.findByRole("menuitem", { name: "Retire this key" }))
    expect(onChange).toHaveBeenLastCalledWith({
      operation: "GenerateHmacKey",
      keyringAction: "Retire",
      keyId: 1,
    })
  })

  it("refuses to retire a key webhook keys still validate under, and says until when", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue(
      keyring([current(2), previous(1, { webhookKeyCount: 3, webhookGraceUntilUtc: FUTURE })])
    )
    const { onChange } = renderPanel()

    await screen.findByText("Previous")
    const row = rowOf(1)
    expect(
      within(row).getByText(/3 webhook keys still validate under it until/)
    ).toBeInTheDocument()
    await user.click(within(row).getByRole("button", { name: "Actions for key 1" }))
    const retire = await screen.findByRole("menuitem", { name: "Retire this key" })
    expect(retire).toHaveAttribute("data-disabled")
    await user.click(retire)
    expect(onChange).not.toHaveBeenCalled()
  })

  it("refuses to reinstate a leaked key, or one whose window has closed", async () => {
    get.mockResolvedValue(
      keyring([
        current(3),
        previous(1, { compromised: true, verifyUntilUtc: PAST }),
        previous(2, { verifyUntilUtc: PAST }),
      ])
    )
    renderPanel()

    await screen.findAllByText("Previous")
    expect(
      within(rowOf(1)).getByText(/Marked leaked by an emergency rotation/)
    ).toBeInTheDocument()
    expect(within(rowOf(1)).getByText("Compromised")).toBeInTheDocument()
    expect(
      within(rowOf(2)).getByText("Its overlap window has closed: it can no longer be reinstated.")
    ).toBeInTheDocument()
  })

  it("says this process and the file disagree, with both key ids", async () => {
    get.mockResolvedValue(keyring([current(2), previous(1)], false))
    renderPanel()

    expect(await screen.findByText("This process and the secrets file disagree")).toBeInTheDocument()
    expect(
      screen.getByText(/The secrets file names key 2 as current, but this process issues under key 1/)
    ).toBeInTheDocument()
  })

  it("on a deployment that cannot hold a keyring, says so without offering a retry", async () => {
    get.mockResolvedValue({
      error: {
        status: 409,
        code: "Secret.KeyringUnavailable",
        detail: "The keyring is managed only in the encrypted secrets file.",
      },
    })
    renderPanel()

    expect(
      await screen.findByText("The keyring cannot be managed on this deployment")
    ).toBeInTheDocument()
    expect(
      screen.getByText("The keyring is managed only in the encrypted secrets file.")
    ).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Stage a new key" })).not.toBeInTheDocument()
  })

  it("on any other failure, says the keyring could not be read and offers a retry", async () => {
    const user = userEvent.setup()
    get.mockResolvedValueOnce({ error: { status: 500 } })
    renderPanel()

    expect(await screen.findByText("The keyring could not be read")).toBeInTheDocument()
    get.mockResolvedValueOnce(keyring([current(1)]))
    await user.click(screen.getByRole("button", { name: "Retry" }))
    expect(await screen.findByRole("button", { name: "Stage a new key" })).toBeEnabled()
  })
})
