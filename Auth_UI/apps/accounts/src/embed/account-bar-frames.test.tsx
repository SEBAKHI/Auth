import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

import { AccountBar } from "./account-bar"
import { AccountBarPanel } from "./account-bar-panel"
import { EmbedProviders } from "./embed-root"
import { NAMESPACE } from "./protocol"

/**
 * The bar's two frames as a linked application frames them: the parent is a page on
 * https://localhost (the IAM site in tests), the person comes from the account bar read.
 */

const PARENT = "https://localhost:5180"

const PERSON = {
  name: "Maya Haddad",
  email: "maya@example.com",
  profileImageUrl: null,
  preferredLanguage: null,
  theme: null,
  applications: [
    { id: "1", name: "Crm", logoUrl: null, logoUrlDark: null, launchUrl: "https://crm.example.com/" },
    { id: "2", name: "Broken", logoUrl: null, logoUrlDark: null, launchUrl: "javascript:alert(1)" },
  ],
}

let postMessage: ReturnType<typeof vi.fn>
let parent: Window
let answer: () => Response

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } })
}

beforeEach(() => {
  postMessage = vi.fn()
  parent = { postMessage } as unknown as Window
  vi.spyOn(window, "parent", "get").mockReturnValue(parent)
  answer = () => json(200, PERSON)
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith("/api/v1/Auth/account-bar")) {
        expect(init?.credentials).toBe("include")
        return answer()
      }
      return json(404, {})
    })
  )
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function at(page: string) {
  window.history.replaceState({}, "", page)
}

function renderFrame(frame: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <EmbedProviders parentOrigin={PARENT}>{frame}</EmbedProviders>
    </QueryClientProvider>
  )
}

/** What the frame asked of the loader, without the size reports. */
function requests() {
  return postMessage.mock.calls
    .filter(([message]) => message.type !== "size")
    .map(([message, origin]) => ({ ...message, origin }))
}

function host(data: Record<string, unknown>) {
  act(() => {
    window.dispatchEvent(new MessageEvent("message", { data: { ns: NAMESPACE, ...data }, origin: PARENT, source: parent }))
  })
}

describe("the bar frame", () => {
  it("draws the grid button and the avatar, and asks the loader to open their panels", async () => {
    at("/embed/v1/bar.html")
    const user = userEvent.setup()
    renderFrame(<AccountBar />)

    const grid = await screen.findByRole("button", { name: "Applications" })
    const avatar = screen.getByRole("button", { name: "Maya Haddad" })
    expect(grid).toHaveAttribute("aria-expanded", "false")

    await user.click(grid)
    await user.click(avatar)
    expect(requests()).toEqual([
      { ns: NAMESPACE, type: "toggle", panel: "apps", origin: PARENT },
      { ns: NAMESPACE, type: "toggle", panel: "account", origin: PARENT },
    ])

    // The loader says which panel is open; the buttons announce it, and take the focus back.
    host({ type: "state", open: "apps", focus: false })
    expect(grid).toHaveAttribute("aria-expanded", "true")
    host({ type: "state", open: null, focus: true })
    expect(grid).toHaveAttribute("aria-expanded", "false")
    expect(grid).toHaveFocus()
  })

  it.each([
    ["avatar", "Applications", "Maya Haddad"],
    ["apps", "Maya Haddad", "Applications"],
  ])("hide=%s leaves the other button alone", async (hide, shown, hidden) => {
    at(`/embed/v1/bar.html?hide=${hide}`)
    renderFrame(<AccountBar />)
    expect(await screen.findByRole("button", { name: shown })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: hidden })).toBeNull()
  })

  it("offers Sign in in the avatar's place when there is no single sign-on session, and no grid", async () => {
    at("/embed/v1/bar.html")
    answer = () => json(401, { code: "Http.Unauthenticated" })
    const user = userEvent.setup()
    renderFrame(<AccountBar />)

    await user.click(await screen.findByRole("button", { name: "Sign in" }))
    expect(screen.queryByRole("button", { name: "Applications" })).toBeNull()
    expect(requests()).toEqual([{ ns: NAMESPACE, type: "signin", origin: PARENT }])
  })

  it("draws nothing when the read fails: a refusal is not being signed out", async () => {
    at("/embed/v1/bar.html")
    answer = () => json(429, { code: "Http.TooManyRequests" })
    const { container } = renderFrame(<AccountBar />)
    await waitFor(() => expect(container.querySelector("[data-slot=skeleton]")).toBeNull())
    expect(screen.queryByRole("button")).toBeNull()
  })
})

describe("the panel frames", () => {
  it("draws the person's tiles, drops one whose address is not a web address, and closes when one is followed", async () => {
    at("/embed/v1/panel.html?panel=apps")
    const user = userEvent.setup()
    renderFrame(<AccountBarPanel />)

    const tile = await screen.findByRole("link", { name: /Crm/ })
    // The panel's content mounts after its frame's first effects: it still reports its
    // size, with the room kept for its shadow, or the loader never shows it.
    expect(postMessage).toHaveBeenCalledWith(
      expect.objectContaining({ ns: NAMESPACE, type: "size", inset: 16 }),
      PARENT
    )
    expect(tile).toHaveAttribute("href", "https://crm.example.com/")
    expect(tile).toHaveAttribute("target", "_blank")
    expect(screen.queryByText("Broken")).toBeNull()

    tile.addEventListener("click", (event) => event.preventDefault())
    await user.click(tile)
    expect(requests()).toContainEqual({ ns: NAMESPACE, type: "close", focusBar: false, origin: PARENT })
  })

  it("asks to close with the focus back on the bar on Escape", async () => {
    at("/embed/v1/panel.html?panel=apps")
    const user = userEvent.setup()
    renderFrame(<AccountBarPanel />)
    await screen.findByRole("link", { name: /Crm/ })
    await user.keyboard("{Escape}")
    expect(requests()).toContainEqual({ ns: NAMESPACE, type: "close", focusBar: true, origin: PARENT })
  })

  it("shows the person and Profile, and Sign out only with the application's sign-out address", async () => {
    at("/embed/v1/panel.html?panel=account&signout=1")
    const user = userEvent.setup()
    renderFrame(<AccountBarPanel />)

    expect(await screen.findByText("maya@example.com")).toBeInTheDocument()
    expect(screen.getByRole("menuitem", { name: /Profile/ })).toHaveAttribute("href", "/profile")
    await user.click(screen.getByRole("menuitem", { name: "Sign out" }))
    expect(requests()).toContainEqual({ ns: NAMESPACE, type: "signout", origin: PARENT })
  })

  it("has no Sign out without the address", async () => {
    at("/embed/v1/panel.html?panel=account")
    renderFrame(<AccountBarPanel />)
    expect(await screen.findByText("maya@example.com")).toBeInTheDocument()
    expect(screen.queryByRole("menuitem", { name: "Sign out" })).toBeNull()
  })
})
