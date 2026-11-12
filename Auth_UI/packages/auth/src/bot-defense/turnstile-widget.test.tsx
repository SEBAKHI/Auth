import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n, { applyLanguage, type LanguageCode } from "@authsystem/i18n"

/**
 * The bot check of the sign-in pages (S13, BD9): Cloudflare Turnstile through
 * `useBotCheck`, with the API answered at the typed client and `window.turnstile`
 * replaced by a recorder of what the page asked Cloudflare's script to do.
 *
 * What it pins: nothing loads while the server names no sitekey; one widget,
 * `interaction-only`, made for the action of the request it serves, in the
 * page's language; Monitor never waits and Enforce does; a token goes out once
 * and the widget makes a fresh one; and the vendor's Retry column decides when
 * the person is told to do something else.
 */

const mocks = vi.hoisted(() => ({ get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => mocks.get(...args) },
}))

import { ENFORCED_WAIT_MS, useBotCheck, type BotAction, type BotTokenResult } from "./bot-check"
import {
  forgetTurnstileForTests,
  TURNSTILE_SCRIPT_URL,
  type TurnstileApi,
  type TurnstileRenderOptions,
} from "./turnstile-script"

const SITE_KEY = "1x00000000000000000000AA"

interface FakeWidget {
  id: string
  options: TurnstileRenderOptions
  resets: number
  removed: boolean
}

let widgets: FakeWidget[] = []
const live = () => widgets.filter((widget) => !widget.removed)

const fakeTurnstile: TurnstileApi = {
  render: (_container, options) => {
    const widget = { id: `w${widgets.length + 1}`, options, resets: 0, removed: false }
    widgets.push(widget)
    return widget.id
  },
  reset: (id) => {
    const widget = widgets.find((candidate) => candidate.id === id)
    if (widget) widget.resets += 1
  },
  remove: (id) => {
    const widget = widgets.find((candidate) => candidate.id === id)
    if (widget) widget.removed = true
  },
}

function answer(config: { mode: string; siteKey: string | null } | "fails") {
  mocks.get.mockImplementation(async (path: string) => {
    if (path !== "/api/v1/Platform/bot-defense") throw new Error(`unexpected GET ${path}`)
    if (config === "fails") return { data: undefined, error: { status: 503 } }
    return { data: config, error: undefined }
  })
}

function Harness({ expected, results }: { expected: BotAction; results: BotTokenResult[] }) {
  const check = useBotCheck(expected)
  return (
    <div>
      {check.field}
      <button type="button" onClick={async () => results.push(await check.prepare("login"))}>
        prepare login
      </button>
      <button type="button" onClick={async () => results.push(await check.prepare("email-code"))}>
        prepare code
      </button>
      <button type="button" onClick={() => check.spent()}>
        spent
      </button>
    </div>
  )
}

function renderCheck(expected: BotAction = "login") {
  const results: BotTokenResult[] = []
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <Harness expected={expected} results={results} />
    </QueryClientProvider>
  )
  return results
}

async function oneLiveWidget() {
  await waitFor(() => expect(live()).toHaveLength(1))
  return live()[0]
}

const field = () => screen.queryByTestId("bot-check")
const scripts = () => document.head.querySelectorAll(`script[src="${TURNSTILE_SCRIPT_URL}"]`)

beforeEach(async () => {
  await i18n.changeLanguage("en")
  widgets = []
  window.turnstile = fakeTurnstile
  forgetTurnstileForTests()
  mocks.get.mockReset()
})

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  delete window.turnstile
  for (const script of scripts()) script.remove()
})

describe("nothing from Cloudflare while there is nothing to check", () => {
  it.each([
    { mode: "Off", siteKey: null },
    { mode: "Enforce", siteKey: null },
    { mode: "Monitor", siteKey: "   " },
    { mode: "Off", siteKey: SITE_KEY },
  ])("renders no field and loads no script for %o", async (config) => {
    // The script would be inserted if anything asked for it.
    delete window.turnstile
    answer(config)
    const results = renderCheck()
    const user = userEvent.setup()

    await waitFor(() => expect(mocks.get).toHaveBeenCalled())
    await user.click(screen.getByRole("button", { name: "prepare login" }))

    await waitFor(() => expect(results).toEqual([{ proceed: true }]))
    expect(field()).toBeNull()
    expect(scripts()).toHaveLength(0)
    expect(widgets).toHaveLength(0)
  })

  it("sends the request without a token when the configuration cannot be read", async () => {
    answer("fails")
    const results = renderCheck()
    const user = userEvent.setup()

    // Said, with a way to try again, rather than left as a check that never shows.
    expect(await screen.findByText("The security check isn't ready")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "prepare login" }))

    await waitFor(() => expect(results).toEqual([{ proceed: true }]))
    expect(widgets).toHaveLength(0)
  })
})

describe("one widget, made for the request it serves", () => {
  it("renders Cloudflare's widget interaction-only, for the page's action", async () => {
    answer({ mode: "Monitor", siteKey: SITE_KEY })
    renderCheck("login")

    const widget = await oneLiveWidget()
    expect(widget.options).toMatchObject({
      sitekey: SITE_KEY,
      action: "login",
      appearance: "interaction-only",
      size: "flexible",
      retry: "auto",
      "refresh-expired": "auto",
      language: "en",
      theme: "light",
    })
    // Unseen while Cloudflare asks nothing: out of the form's flow, still in the page.
    expect(field()).toHaveAttribute("data-state", "quiet")
    expect(field()).toHaveClass("sr-only")
    expect(field()).toHaveAccessibleName("Security check")
  })

  it("shows the widget's box when Cloudflare asks for a click, and hides it after", async () => {
    answer({ mode: "Monitor", siteKey: SITE_KEY })
    renderCheck()
    const widget = await oneLiveWidget()

    act(() => widget.options["before-interactive-callback"]())
    expect(field()).toHaveAttribute("data-state", "interactive")
    expect(field()).not.toHaveClass("sr-only")

    act(() => widget.options["after-interactive-callback"]())
    expect(field()).toHaveClass("sr-only")
  })

  it.each([
    ["ur", "en"],
    ["zh", "zh-cn"],
    ["ar", "ar"],
    ["fa", "fa"],
  ] as const)("asks for the widget in the page's language: %s gives %s", async (page, widgetLanguage) => {
    await applyLanguage(page as LanguageCode)
    // The page really is in that language: a hidden one would fall back to English.
    expect(i18n.language).toBe(page)
    answer({ mode: "Monitor", siteKey: SITE_KEY })
    renderCheck()

    expect((await oneLiveWidget()).options.language).toBe(widgetLanguage)
  })

  it("loads Cloudflare's script once for the page, however often the widget is made", async () => {
    delete window.turnstile
    answer({ mode: "Enforce", siteKey: SITE_KEY })
    renderCheck("login")
    const user = userEvent.setup()

    await waitFor(() => expect(scripts()).toHaveLength(1))
    // Made again for another action while the script is still on its way: the
    // script already loading serves the new widget too.
    await user.click(screen.getByRole("button", { name: "prepare code" }))
    expect(scripts()).toHaveLength(1)

    window.turnstile = fakeTurnstile
    act(() => {
      scripts()[0].dispatchEvent(new Event("load"))
    })
    await waitFor(() => expect(live()[0]?.options.action).toBe("email-code"))
    expect(live()).toHaveLength(1)

    // And once loaded, every later widget uses it: no second script.
    await user.click(screen.getByRole("button", { name: "spent" }))
    await waitFor(() => expect(live()[0]?.options.action).toBe("login"))
    expect(scripts()).toHaveLength(1)
  })

  it("renders the same single widget again for another action, and back after the spend", async () => {
    answer({ mode: "Enforce", siteKey: SITE_KEY })
    const results = renderCheck("login")
    const user = userEvent.setup()
    const first = await oneLiveWidget()
    expect(first.options.action).toBe("login")

    await user.click(screen.getByRole("button", { name: "prepare code" }))
    await waitFor(() => expect(live()[0]?.options.action).toBe("email-code"))
    expect(live()).toHaveLength(1)
    expect(first.removed).toBe(true)

    // A late token from the widget made for the other action is not this request's.
    act(() => first.options.callback("late-login-token"))
    act(() => live()[0].options.callback("code-token"))
    await waitFor(() => expect(results).toEqual([{ proceed: true, token: "code-token" }]))

    await user.click(screen.getByRole("button", { name: "spent" }))
    await waitFor(() => expect(live()[0]?.options.action).toBe("login"))
    expect(live()).toHaveLength(1)
  })
})

describe("Monitor never waits; Enforce does", () => {
  it("Monitor: sends the ready token once, and sends without one rather than wait", async () => {
    answer({ mode: "Monitor", siteKey: SITE_KEY })
    const results = renderCheck()
    const user = userEvent.setup()
    const widget = await oneLiveWidget()
    const prepare = () => user.click(screen.getByRole("button", { name: "prepare login" }))

    await prepare()
    await waitFor(() => expect(results).toHaveLength(1))
    act(() => widget.options.callback("token-1"))
    await prepare()
    await waitFor(() => expect(results).toHaveLength(2))
    await prepare()
    await waitFor(() => expect(results).toHaveLength(3))

    expect(results).toEqual([{ proceed: true }, { proceed: true, token: "token-1" }, { proceed: true }])
  })

  it("Enforce: waits for the widget's token and sends it", async () => {
    answer({ mode: "Enforce", siteKey: SITE_KEY })
    const results = renderCheck()
    const user = userEvent.setup()
    const widget = await oneLiveWidget()

    await user.click(screen.getByRole("button", { name: "prepare login" }))
    expect(results).toEqual([])
    act(() => widget.options.callback("token-1"))

    await waitFor(() => expect(results).toEqual([{ proceed: true, token: "token-1" }]))
  })

  it("Enforce: with no token in time, holds the request and asks for the check", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    answer({ mode: "Enforce", siteKey: SITE_KEY })
    const results = renderCheck()
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    await oneLiveWidget()

    await user.click(screen.getByRole("button", { name: "prepare login" }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(ENFORCED_WAIT_MS)
    })

    await waitFor(() => expect(results).toEqual([{ proceed: false }]))
    expect(field()).toHaveAttribute("data-state", "incomplete")
    expect(field()).not.toHaveClass("sr-only")
    expect(screen.getByText("Complete the security check above, then try again.")).toBeInTheDocument()
  })

  it("after every spend, the widget makes a fresh token and the next request waits for it", async () => {
    answer({ mode: "Enforce", siteKey: SITE_KEY })
    const results = renderCheck()
    const user = userEvent.setup()
    const widget = await oneLiveWidget()

    await user.click(screen.getByRole("button", { name: "prepare login" }))
    act(() => widget.options.callback("token-1"))
    await waitFor(() => expect(results).toHaveLength(1))
    await user.click(screen.getByRole("button", { name: "spent" }))
    expect(widget.resets).toBe(1)

    await user.click(screen.getByRole("button", { name: "prepare login" }))
    expect(results).toHaveLength(1)
    act(() => widget.options.callback("token-2"))

    await waitFor(() =>
      expect(results).toEqual([
        { proceed: true, token: "token-1" },
        { proceed: true, token: "token-2" },
      ])
    )
  })
})

describe("when the widget fails, Cloudflare's Retry column decides", () => {
  it.each(["110100", "110110", "110200", "200100", "400020", "400021", "400070"])(
    "%s (no retry): tells the person, and the waiting request stops at once",
    async (code) => {
      answer({ mode: "Enforce", siteKey: SITE_KEY })
      const results = renderCheck()
      const user = userEvent.setup()
      const widget = await oneLiveWidget()

      await user.click(screen.getByRole("button", { name: "prepare login" }))
      let handled: boolean | undefined
      act(() => {
        handled = widget.options["error-callback"](code)
      })

      expect(handled).toBe(true)
      await waitFor(() => expect(results).toEqual([{ proceed: false }]))
      expect(screen.getByText("The security check can't run here")).toBeInTheDocument()
      expect(field()).not.toHaveClass("sr-only")
    }
  )

  it.each(["110600", "110620", "200500", "300010", "600010"])(
    "%s (retry): the widget retries by itself, says nothing, and the waiting request keeps waiting",
    async (code) => {
      answer({ mode: "Enforce", siteKey: SITE_KEY })
      const results = renderCheck()
      const user = userEvent.setup()
      const widget = await oneLiveWidget()

      await user.click(screen.getByRole("button", { name: "prepare login" }))
      act(() => {
        widget.options["error-callback"](code)
      })
      expect(screen.queryByTestId("bot-check-problem")).toBeNull()
      expect(results).toEqual([])

      act(() => widget.options.callback("after-retry"))
      await waitFor(() => expect(results).toEqual([{ proceed: true, token: "after-retry" }]))
    }
  )

  it("a browser Turnstile does not support is told so", async () => {
    answer({ mode: "Monitor", siteKey: SITE_KEY })
    renderCheck()
    const widget = await oneLiveWidget()

    act(() => widget.options["unsupported-callback"]())

    expect(screen.getByText("The security check can't run here")).toBeInTheDocument()
  })

  it("a script that does not load is said, and Retry loads it again", async () => {
    delete window.turnstile
    answer({ mode: "Enforce", siteKey: SITE_KEY })
    renderCheck()
    const user = userEvent.setup()

    await waitFor(() => expect(scripts()).toHaveLength(1))
    act(() => {
      scripts()[0].dispatchEvent(new Event("error"))
    })
    expect(await screen.findByText("The security check didn't load")).toBeInTheDocument()
    expect(scripts()).toHaveLength(0)

    await user.click(screen.getByRole("button", { name: "Retry" }))
    await waitFor(() => expect(scripts()).toHaveLength(1))
    window.turnstile = fakeTurnstile
    act(() => {
      scripts()[0].dispatchEvent(new Event("load"))
    })

    await oneLiveWidget()
    expect(screen.queryByText("The security check didn't load")).toBeNull()
  })

  it("a blocked widget is made again by Retry", async () => {
    answer({ mode: "Monitor", siteKey: SITE_KEY })
    renderCheck()
    const user = userEvent.setup()
    const first = await oneLiveWidget()
    act(() => {
      first.options["error-callback"]("110200")
    })

    await user.click(screen.getByRole("button", { name: "Retry" }))

    await waitFor(() => expect(first.removed).toBe(true))
    expect(live()).toHaveLength(1)
    expect(screen.queryByTestId("bot-check-problem")).toBeNull()
  })
})
