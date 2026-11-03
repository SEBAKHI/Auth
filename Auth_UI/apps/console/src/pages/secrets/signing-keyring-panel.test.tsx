import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const get = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => get(...args) },
}))

import { SigningKeyringPanel } from "./signing-keyring-panel"

type Entry = {
  kid: string
  state: "Current" | "Next" | "Previous" | "Compromised"
  migrated: boolean
  createdAtUtc: string | null
  publishedAtUtc: string | null
  verifyUntilUtc: string | null
  compromised: boolean
  inFile: boolean
  loadedInProcess: boolean
  acceptedInProcess: boolean
}

const CURRENT = "Yp3yQnXc0bK1w0b0q5Zb2l1i8m4kX6s9TqvW7cJr1aA"
const STAGED = "q8H5w2n0tP4vR7yZ1aB3cD5eF7gH9iJ0kL2mN4oP6qR"
const FUTURE = "2999-01-01T00:00:00Z"
const PAST = "2001-01-01T00:00:00Z"

function current(kid = CURRENT, migrated = false): Entry {
  return {
    kid,
    state: "Current",
    migrated,
    createdAtUtc: migrated ? null : "2026-10-01T00:00:00Z",
    publishedAtUtc: null,
    verifyUntilUtc: null,
    compromised: false,
    inFile: true,
    loadedInProcess: true,
    acceptedInProcess: true,
  }
}

function staged(loadedInProcess: boolean, publishedAtUtc: string | null = null): Entry {
  return {
    ...current(STAGED),
    state: "Next",
    publishedAtUtc,
    loadedInProcess,
    acceptedInProcess: loadedInProcess,
  }
}

function read(keys: Entry[], overrides: Record<string, unknown> = {}) {
  const currentKey = keys.find((key) => key.state === "Current")
  const next = keys.find((key) => key.state === "Next")
  return {
    data: {
      currentKeyId: 1,
      processCurrentKeyId: 1,
      inSync: true,
      keys: [],
      rsa: {
        available: true,
        keySource: "Jwt:PrivateKeyPem",
        currentKid: currentKey?.kid,
        processCurrentKid: currentKey?.kid ?? CURRENT,
        nextKid: next?.kid ?? null,
        persisted: !currentKey?.migrated,
        promoteAvailableAtUtc: null,
        inSync: true,
        keys,
        ...overrides,
      },
    },
  }
}

function renderPanel() {
  const onChange = vi.fn()
  const onStageImport = vi.fn()
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  render(
    <QueryClientProvider client={client}>
      <SigningKeyringPanel busy={false} onChange={onChange} onStageImport={onStageImport} />
    </QueryClientProvider>
  )
  return { onChange, onStageImport }
}

function rowOf(kid: string) {
  const cell = screen.getAllByRole("cell").find((c) => c.textContent === kid)
  if (!cell) throw new Error(`no row for key ${kid}`)
  return cell.closest("tr") as HTMLElement
}

describe("SigningKeyringPanel", () => {
  beforeEach(() => {
    get.mockReset()
  })

  it("on a deployment that never rotated, names the migrated key and says why promote waits", async () => {
    get.mockResolvedValue(read([current("auth-key-1", true)]))
    renderPanel()

    expect(await screen.findByText("Unknown (migrated key)")).toBeInTheDocument()
    expect(screen.getByText(/key auth-key-1 is published under the name it always had/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Stage a key" })).toBeEnabled()
    expect(screen.getByRole("button", { name: "Promote" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Emergency promote" })).toBeDisabled()
    expect(screen.getByText("No key is staged.")).toBeInTheDocument()
    expect(screen.getByText(/An emergency promotes a staged key: stage a new key first/)).toBeInTheDocument()
    expect(within(rowOf("auth-key-1")).getByText("Signs every token")).toBeInTheDocument()
  })

  it("asks for the restart while the staged key is not loaded, then for the 24 hours, naming the time", async () => {
    get.mockResolvedValueOnce(read([current(), staged(false)]))
    renderPanel()

    expect(await screen.findByText(/Restart the API so it loads and publishes the staged key/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Promote" })).toBeDisabled()
    expect(screen.getByText(/closes the leaked key only when the API restarts/)).toBeInTheDocument()
  })

  it("disables promote before the 24 hours with the date it opens", async () => {
    get.mockResolvedValue(read([current(), staged(true, "2999-01-01T00:00:00Z")], { promoteAvailableAtUtc: FUTURE }))
    renderPanel()

    expect(await screen.findByText(/The staged key can be promoted from/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Promote" })).toBeDisabled()
  })

  it("enables promote once the staged key is loaded and published for 24 hours, and sends its kid", async () => {
    get.mockResolvedValue(read([current(), staged(true, PAST)], { promoteAvailableAtUtc: PAST }))
    const { onChange } = renderPanel()
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: "Promote" }))

    expect(onChange).toHaveBeenCalledWith({ operation: "GenerateRsaKey", keyringAction: "Promote", kid: STAGED })
  })

  it("never lets a leaked key read as closed while this process still accepts it", async () => {
    const leaked: Entry = {
      ...current("leaked-kid"),
      state: "Compromised",
      compromised: true,
      verifyUntilUtc: PAST,
      acceptedInProcess: true,
    }
    get.mockResolvedValue(read([current(), leaked]))
    renderPanel()

    expect(await screen.findByText("A leaked key is still accepted by this API process")).toBeInTheDocument()
    expect(screen.getByText(/Key leaked-kid is marked compromised.*Restart the API now to close it/)).toBeInTheDocument()
  })

  it("renders a kid with no direction attribute, cut to its cell, the full kid on hover", async () => {
    get.mockResolvedValue(read([current()]))
    renderPanel()

    const kid = await screen.findByText(CURRENT)
    expect(kid).toHaveAttribute("title", CURRENT)
    expect(kid.closest("[dir]")?.tagName ?? null).not.toBe("TD")
    expect(kid).not.toHaveAttribute("dir")
  })

  it("on a key from another source, says where it comes from and offers nothing", async () => {
    get.mockResolvedValue(
      read([current("auth-key-1", true)], { available: false, unavailableReason: "ExternalSource", keySource: "Jwt:PrivateKeyPath" })
    )
    renderPanel()

    expect(await screen.findByText(/The signing key comes from Jwt:PrivateKeyPath/)).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Stage a key" })).not.toBeInTheDocument()
  })

  it("a previous key this API process still signs with offers no retire, and says to restart first", async () => {
    const demoted: Entry = { ...current(CURRENT), state: "Previous", verifyUntilUtc: FUTURE }
    get.mockResolvedValue(read([current(STAGED), demoted], { processCurrentKid: CURRENT }))
    renderPanel()

    const reason = "This API process still signs with this key: restart it before retiring the key."
    expect(await screen.findByText(reason)).toBeInTheDocument()
    expect(within(rowOf(CURRENT)).getByText(reason)).toBeInTheDocument()
    expect(
      within(rowOf(CURRENT)).queryByRole("button", { name: `Actions for key ${CURRENT}` })
    ).not.toBeInTheDocument()
  })
})
