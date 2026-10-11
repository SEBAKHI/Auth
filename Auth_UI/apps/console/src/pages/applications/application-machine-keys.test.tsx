import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  cleanup,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

const get = vi.fn()
const post = vi.fn()
const hasPermission = vi.fn<(permission: string) => boolean>(() => true)

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: (...args: unknown[]) => post(...args),
    PUT: vi.fn(),
    DELETE: vi.fn(),
  },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ hasPermission }),
}))

import { ApplicationMachineKeysTab } from "./application-machine-keys"

const APP_ID = "11111111-1111-1111-1111-111111111111"
const PEM =
  "-----BEGIN PRIVATE KEY-----\nMIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg-test-only\n-----END PRIVATE KEY-----\n"
const KID = "q0Wd9Ywm0y8Kx5pWQZ1tQ8e3Sx0cVd1Jg2mN4kq7rTU"

const activeKey = {
  id: "22222222-2222-2222-2222-222222222222",
  applicationId: APP_ID,
  kid: "existing-kid",
  name: "Production server",
  algorithm: "ES256",
  createdAt: "2026-10-01T08:00:00Z",
  createdBy: "33333333-3333-3333-3333-333333333333",
  lastUsedAt: null,
  isRevoked: false,
  revokedAt: null,
  revokeReason: null,
}

const generated = {
  id: "44444444-4444-4444-4444-444444444444",
  kid: KID,
  clientId: "edis",
  issuer: "https://auth.example.com",
  tokenEndpoint: "https://auth.example.com/api/v1/auth/token",
  algorithm: "ES256",
  privateKeyPem: PEM,
  createdAt: "2026-10-11T08:00:00Z",
}

function renderTab() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <ApplicationMachineKeysTab appId={APP_ID} />
    </QueryClientProvider>
  )
}

/** Everything the page holds right now, dialogs included, as one string. */
function pageText() {
  const values = [...document.querySelectorAll("textarea, input")].map(
    (element) => (element as HTMLInputElement).value
  )
  return [document.body.textContent ?? "", ...values].join("\n")
}

describe("application machine keys", () => {
  beforeEach(async () => {
    get.mockReset()
    post.mockReset()
    hasPermission.mockReturnValue(true)
    get.mockResolvedValue({ data: [activeKey] })
    await i18n.changeLanguage("en")
  })

  afterEach(() => cleanup())

  it("shows a generated private key once, and closing the result clears it for good", async () => {
    post.mockResolvedValue({ data: generated })
    const user = userEvent.setup()
    renderTab()

    await screen.findByText("existing-kid")
    await user.click(screen.getByRole("button", { name: "Generate key" }))
    const form = await screen.findByRole("dialog", { name: "New machine key" })
    await user.click(within(form).getByRole("button", { name: "Create" }))

    // The request asked the server to generate: no public key was sent.
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0]?.[0]).toBe(
      "/api/v1/applications/{applicationId}/machine-keys"
    )
    expect(post.mock.calls[0]?.[1]).toEqual({
      params: { path: { applicationId: APP_ID } },
      body: { name: null, publicKeyJwk: null },
    })

    const result = await screen.findByRole("dialog", {
      name: "Machine key created",
    })
    expect(within(result).getByLabelText("Private key (PEM)")).toHaveValue(PEM)
    expect(within(result).getByLabelText("Client ID")).toHaveValue("edis")
    expect(within(result).getByLabelText("Key ID (kid)")).toHaveValue(KID)
    expect(
      within(result).getByLabelText("Issuer (the assertion's audience)")
    ).toHaveValue("https://auth.example.com")
    expect(within(result).getByLabelText("Token endpoint")).toHaveValue(
      "https://auth.example.com/api/v1/auth/token"
    )
    expect(pageText()).toContain("BEGIN PRIVATE KEY")

    // The footer button; the corner X closes the same way.
    const closeButtons = within(result).getAllByRole("button", {
      name: "Close",
    })
    expect(closeButtons.length).toBeGreaterThan(0)
    await user.click(closeButtons[closeButtons.length - 1]!)
    await waitFor(() =>
      expect(
        screen.queryByRole("dialog", { name: "Machine key created" })
      ).not.toBeInTheDocument()
    )
    expect(pageText()).not.toContain("BEGIN PRIVATE KEY")

    // Opening the create dialog again shows a fresh form, never the old key.
    await user.click(screen.getByRole("button", { name: "Generate key" }))
    await screen.findByRole("dialog", { name: "New machine key" })
    expect(pageText()).not.toContain("BEGIN PRIVATE KEY")
    expect(pageText()).not.toContain(KID)
  })

  it("downloads the private key as <kid>.pem", async () => {
    post.mockResolvedValue({ data: generated })
    const createObjectURL = vi.fn((blob: Blob) => {
      void blob
      return "blob:machine-key"
    })
    const revokeObjectURL = vi.fn()
    Object.assign(URL, { createObjectURL, revokeObjectURL })
    const clicked: string[] = []
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(function (this: HTMLAnchorElement) {
        clicked.push(this.download)
      })

    const user = userEvent.setup()
    renderTab()
    await user.click(
      await screen.findByRole("button", { name: "Generate key" })
    )
    await user.click(
      within(
        await screen.findByRole("dialog", { name: "New machine key" })
      ).getByRole("button", { name: "Create" })
    )
    const result = await screen.findByRole("dialog", {
      name: "Machine key created",
    })
    await user.click(within(result).getByRole("button", { name: "Download" }))

    expect(clicked).toEqual([`${KID}.pem`])
    expect(await createObjectURL.mock.calls[0]?.[0].text()).toBe(PEM)
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:machine-key")
    click.mockRestore()
  })

  it("registers a pasted public key and shows no private key", async () => {
    post.mockResolvedValue({ data: { ...generated, privateKeyPem: null } })
    const jwk = '{"kty":"EC","crv":"P-256","x":"AAAA","y":"BBBB"}'
    const user = userEvent.setup()
    renderTab()

    await user.click(
      await screen.findByRole("button", { name: "Generate key" })
    )
    const form = await screen.findByRole("dialog", { name: "New machine key" })
    await user.type(within(form).getByLabelText("Name"), "Edge server")
    await user.click(
      within(form).getByRole("radio", { name: "Use my public key" })
    )
    const textarea = within(form).getByLabelText("Public key (JWK)")
    await user.click(textarea)
    await user.paste(jwk)
    await user.click(within(form).getByRole("button", { name: "Create" }))

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0]?.[1]).toMatchObject({
      body: { name: "Edge server", publicKeyJwk: jwk },
    })
    const result = await screen.findByRole("dialog", {
      name: "Machine key created",
    })
    expect(
      within(result).queryByLabelText("Private key (PEM)")
    ).not.toBeInTheDocument()
  })

  it("puts a refused public key on its field", async () => {
    post.mockResolvedValue({
      error: {
        status: 400,
        code: "MachineKey.InvalidPublicKey",
        detail: "The public key must be a new EC P-256 key in JWK form.",
      },
    })
    const user = userEvent.setup()
    renderTab()

    await user.click(
      await screen.findByRole("button", { name: "Generate key" })
    )
    const form = await screen.findByRole("dialog", { name: "New machine key" })
    await user.click(
      within(form).getByRole("radio", { name: "Use my public key" })
    )
    await user.click(within(form).getByLabelText("Public key (JWK)"))
    await user.paste('{"kty":"RSA"}')
    await user.click(within(form).getByRole("button", { name: "Create" }))

    expect(
      await within(form).findByText(/must be a new EC P-256 key/)
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("dialog", { name: "Machine key created" })
    ).not.toBeInTheDocument()
  })

  it("revokes after a confirmation that names the key", async () => {
    post.mockResolvedValue({ data: undefined })
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole("button", { name: "Revoke" }))
    const confirm = await screen.findByRole("alertdialog")
    expect(
      within(confirm).getByText(/Production server stops obtaining tokens/)
    ).toBeInTheDocument()
    expect(post).not.toHaveBeenCalled()

    await user.click(within(confirm).getByRole("button", { name: "Revoke" }))
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0]).toEqual([
      "/api/v1/applications/{applicationId}/machine-keys/{keyId}/revoke",
      { params: { path: { applicationId: APP_ID, keyId: activeKey.id } } },
    ])
  })

  it("offers neither generate nor revoke without the permissions", async () => {
    hasPermission.mockImplementation(
      (permission) => permission === "apikeys:read"
    )
    renderTab()

    await screen.findByText("existing-kid")
    expect(
      screen.queryByRole("button", { name: "Generate key" })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Revoke" })
    ).not.toBeInTheDocument()
  })
})
