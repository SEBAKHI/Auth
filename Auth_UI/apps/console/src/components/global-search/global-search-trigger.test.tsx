import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    hasPermission: () => true,
    user: { id: "11111111-1111-1111-1111-111111111111" },
  }),
}))

import { GlobalSearch } from "./global-search"

function renderTrigger() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  const { container } = render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <GlobalSearch />
      </MemoryRouter>
    </QueryClientProvider>
  )
  const trigger = container.querySelector<HTMLElement>('[data-slot="button"]')
  if (!trigger) throw new Error("No search trigger rendered")
  return trigger
}

/**
 * The header packs four controls beside the breadcrumb trail and every Button
 * is `shrink-0`, so a width this trigger claims on a phone is taken straight
 * out of the page title's share — which is how the title ended up painted over
 * the search pill. The field is a `md`-and-up affordance; below that the
 * trigger is the icon alone.
 */
describe("GlobalSearch trigger", () => {
  it("claims no width below md", () => {
    const trigger = renderTrigger()

    expect(trigger.className).toContain("md:w-48")
    expect(trigger.className).not.toMatch(/(^|\s)w-\d/)
  })

  it("drops its label below md and keeps the control named", () => {
    const trigger = renderTrigger()

    const label = trigger.querySelector("span:not(.sr-only)")
    expect(label?.className).toContain("hidden")
    expect(label?.className).toContain("md:inline")
    // The icon-only form still has to say what it is.
    expect(trigger).toHaveAttribute("aria-label")
    expect(trigger.getAttribute("aria-label")).not.toBe("")
  })
})
