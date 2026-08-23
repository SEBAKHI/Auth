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

/**
 * Nothing inside a scroll pane has been squeezed and silently clipped.
 *
 * The failure this catches, seen in production on the policy editor: a pane is
 * made a fixed-height scroll area (`min-h-0 flex-1 overflow-y-auto` on a column
 * flex container) and `Card` elements are dropped straight into it. A flex item
 * normally refuses to shrink past its own content because `min-height` resolves
 * to `auto` - but that protection is switched off for any box whose overflow is
 * not `visible`, and `Card` carries `overflow-hidden`. So the cards shrink to a
 * fraction of their height, clip every control inside, and the pane never
 * overflows and therefore never grows a scrollbar. Five cards on one screen were
 * rendering at 48px each while their content measured up to 1938px.
 *
 * `expectNoShellOverflow` cannot see any of this: the loss is vertical and the
 * clipping is silent.
 */
export async function expectNoCrushedContent(page: Page, where: string) {
  const crushed = await page.evaluate(() => {
    return [...document.querySelectorAll("*")]
      .filter((el) => {
        if (!(el instanceof HTMLElement)) return false
        if (el.scrollHeight <= el.clientHeight + 1) return false
        const style = getComputedStyle(el)
        // Only silent losses. A child that scrolls its own content is fine.
        if (style.overflowY !== "hidden") return false
        const parent = el.parentElement
        if (!parent) return false
        const parentStyle = getComputedStyle(parent)
        if (parentStyle.display !== "flex") return false
        if (parentStyle.flexDirection !== "column") return false
        return style.flexShrink !== "0"
      })
      .map((el) => ({
        slot: el.getAttribute("data-slot") ?? el.tagName.toLowerCase(),
        label: (
          el.querySelector('[data-slot="card-title"]')?.textContent ?? ""
        ).trim(),
        rendered: Math.round(el.getBoundingClientRect().height),
        natural: el.scrollHeight,
      }))
  })

  expect(
    crushed,
    `${where}: ${crushed.length} element(s) squeezed below their content and clipped - ` +
      crushed
        .map((c) => `${c.slot}${c.label ? ` "${c.label}"` : ""} ${c.rendered}px of ${c.natural}px`)
        .join("; ")
  ).toEqual([])
}

/**
 * A card inside a scroll pane still shows the outline that bounds it.
 *
 * `Card` is not drawn with a border. The preset outlines it with `ring-1` and
 * lifts it with `shadow-md`, and both of those paint OUTSIDE the element's box.
 * Setting `overflow-y` to anything but `visible` also forces `overflow-x` to
 * `auto` - so a pane with no padding clips that outline flush against its own
 * edges, and the card reads as an unbounded slab with no top and no sides.
 *
 * The pane therefore needs padding. One inline side may legitimately show a
 * wide gap: that is the scrollbar gutter, not a fix.
 */
export async function expectCardOutlinesVisible(page: Page, where: string) {
  const clipped = await page.evaluate(() => {
    const panes = [...document.querySelectorAll("*")].filter((el) => {
      if (!(el instanceof HTMLElement) || !el.children.length) return false
      const style = getComputedStyle(el)
      return (
        style.overflowY === "auto" ||
        style.overflowY === "scroll" ||
        style.overflowX === "auto" ||
        style.overflowX === "scroll"
      )
    })

    const found: Array<{ slot: string; label: string; sides: string[] }> = []
    for (const pane of panes) {
      const paneBox = pane.getBoundingClientRect()
      if (paneBox.width < 40 || paneBox.height < 40) continue
      for (const child of pane.children) {
        if (!(child instanceof HTMLElement)) continue
        if (getComputedStyle(child).boxShadow === "none") continue
        const box = child.getBoundingClientRect()
        if (box.width < 40 || box.height < 20) continue
        // Judge only what is inside the scrolled viewport: a card further down
        // the scroll legitimately extends past the bottom edge.
        if (!(box.bottom > paneBox.top && box.top < paneBox.bottom)) continue
        const gaps = {
          top: Math.round(box.top - paneBox.top),
          left: Math.round(box.left - paneBox.left),
          right: Math.round(paneBox.right - box.right),
        }
        const sides = Object.entries(gaps)
          .filter(([, value]) => value >= 0 && value <= 1)
          .map(([side]) => side)
        if (sides.length) {
          found.push({
            slot: child.getAttribute("data-slot") ?? child.tagName.toLowerCase(),
            label: (
              child.querySelector('[data-slot="card-title"]')?.textContent ?? ""
            ).trim(),
            sides,
          })
        }
      }
    }
    return found
  })

  expect(
    clipped,
    `${where}: ${clipped.length} element(s) have their ring/shadow clipped by a scroll pane - ` +
      clipped
        .map((c) => `${c.slot}${c.label ? ` "${c.label}"` : ""} on ${c.sides.join("+")}`)
        .join("; ")
  ).toEqual([])
}
