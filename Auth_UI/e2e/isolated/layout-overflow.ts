import { expect, type Page } from "@playwright/test"

/**
 * The element that actually decides whether a page overflows.
 *
 * Measuring `document.documentElement` here proves nothing: the shell sets
 * `overflow-hidden` on both the sidebar wrapper and the inset, so the document
 * can never report a horizontal overflow no matter how badly the content
 * breaks. A 3000px element dropped into the page leaves the document reading
 * zero while the inset reads 2625 - the assertion below is the one that moves.
 *
 * Source of the clipping: packages/ui/src/common/app-shell.tsx, where
 * `SidebarProvider` is `h-svh overflow-hidden` and `SidebarInset` is
 * `min-h-0 overflow-hidden`. That clipping is deliberate - it is what keeps a
 * wide table from widening the whole document - which is exactly why the
 * measurement has to move inside it.
 */
export const SHELL_INSET = '[data-slot="sidebar-inset"]'

/** How far the shell's content spills past the space it has, in pixels. */
export async function shellOverflow(page: Page): Promise<number> {
  return page.evaluate((selector) => {
    const inset = document.querySelector(selector)
    if (!(inset instanceof HTMLElement)) {
      throw new Error(`no element matched ${selector}`)
    }
    return inset.scrollWidth - inset.clientWidth
  }, SHELL_INSET)
}

/**
 * Nothing in the shell is clipped out of reach at this width.
 *
 * WCAG 2.2 SC 1.4.10 (Reflow) is the rule behind it: content must not require
 * scrolling in two directions. A table that scrolls inside its own container is
 * fine and stays fine here, because the container absorbs the width before it
 * reaches the inset.
 */
export async function expectNoShellOverflow(page: Page, where: string) {
  const overflow = await shellOverflow(page)
  expect(overflow, `${where}: content spills ${overflow}px past the shell`).toBe(0)
}
