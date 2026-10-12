import { createHash } from "node:crypto"

import { act, cleanup, render } from "@testing-library/react"

/*
 * Collects, from the libraries INSTALLED in this workspace, the text of every
 * inline <style> they put in a page - each state a browser checks against the
 * CSP - and hashes it (card S02, "أداة الجمع").
 *
 * Both applications' style-src allows these texts by hash and nothing else:
 * no 'unsafe-inline' (card S02, target 1). The values are computed here,
 * never typed: `pnpm csp:collect` rewrites apps/<app>/csp/inline-styles.json,
 * inline-style-inventory.test.ts fails when the file and this collector
 * disagree, csp-style-policy.test.ts fails when web.config does not carry
 * exactly these hashes, and scripts/seal-web-config.mjs refuses a build whose
 * bundle does not contain every text (or contains a new style site).
 *
 * Only CONSTANT texts belong here. A stylesheet whose text is computed at run
 * time (style-mod, react-style-singleton, ChartStyle, the platform appearance)
 * cannot be hashed; it is an adopted stylesheet instead
 * (packages/ui/src/hooks/use-adopted-style-sheet.ts and patches/).
 *
 * A browser checks an inline <style> when it becomes part of the document,
 * and again whenever its text changes while it is. That is what is recorded:
 * an element's text when it (or an ancestor) is inserted into a connected
 * parent, and its text after each change made while connected - including an
 * empty one, which Chromium checks too (S02 spike item 2: an empty <style> is
 * refused under a strict style-src, so its hash is listed).
 */

export const STYLE_SOURCES = ["sonner", "radix-scroll-area", "radix-select", "input-otp"] as const
export type StyleSource = (typeof STYLE_SOURCES)[number]

export type AppName = "console" | "accounts"

/**
 * The injectors that reach each application's bundle. Kept by hand, and held
 * honest from both sides at build time: a source listed here that is not in
 * the bundle fails the seal's "text present in the bundle" check, and one
 * missing here changes the bundle's style-site count (csp/bundle-style-sites.json).
 */
export const APP_STYLE_SOURCES: Record<AppName, readonly StyleSource[]> = {
  // Toasts everywhere; ScrollArea in the shell and sheets; Select in forms and
  // filters; InputOTP in the profile's two-factor and deletion steps.
  console: ["sonner", "radix-scroll-area", "radix-select", "input-otp"],
  // The same four, through packages/account and packages/auth: toasts, the
  // registration and account pages' Select, ScrollArea in sheets, and every
  // code field (two-factor, email codes).
  accounts: ["sonner", "radix-scroll-area", "radix-select", "input-otp"],
}

/** The CSP source expression's value for a text: base64 SHA-256 of its UTF-8 bytes. */
export function cspHash(text: string) {
  return `sha256-${createHash("sha256").update(text, "utf8").digest("base64")}`
}

function stylesIn(node: Node): HTMLStyleElement[] {
  if (node instanceof HTMLStyleElement) return [node]
  return node instanceof Element || node instanceof DocumentFragment
    ? [...node.querySelectorAll("style")]
    : []
}

/**
 * Records every inline <style> text a browser would check while `run` runs,
 * by wrapping the DOM's insertion points and the text setters. Restores them
 * afterwards whatever happens.
 */
export async function recordInlineStyles(run: () => Promise<void> | void): Promise<string[]> {
  const seen: string[] = []
  const record = (style: HTMLStyleElement) => {
    if (style.isConnected) seen.push(style.textContent ?? "")
  }

  const appendChild = Node.prototype.appendChild
  const insertBefore = Node.prototype.insertBefore
  const textContent = Object.getOwnPropertyDescriptor(Node.prototype, "textContent")!
  const innerHTML = Object.getOwnPropertyDescriptor(Element.prototype, "innerHTML")!

  function afterInsert(parent: Node, node: Node) {
    if (parent instanceof HTMLStyleElement) record(parent)
    else if (parent.isConnected) stylesIn(node).forEach(record)
  }

  Node.prototype.appendChild = function <T extends Node>(this: Node, node: T): T {
    const result = appendChild.call(this, node) as T
    afterInsert(this, node)
    return result
  }
  Node.prototype.insertBefore = function <T extends Node>(this: Node, node: T, child: Node | null): T {
    const result = insertBefore.call(this, node, child) as T
    afterInsert(this, node)
    return result
  }
  Object.defineProperty(Node.prototype, "textContent", {
    ...textContent,
    set(this: Node, value: string | null) {
      textContent.set!.call(this, value)
      if (this instanceof HTMLStyleElement) record(this)
    },
  })
  // React writes dangerouslySetInnerHTML through innerHTML.
  Object.defineProperty(Element.prototype, "innerHTML", {
    ...innerHTML,
    set(this: Element, value: string) {
      innerHTML.set!.call(this, value)
      if (this instanceof HTMLStyleElement) record(this)
    },
  })

  try {
    await run()
  } finally {
    Node.prototype.appendChild = appendChild
    Node.prototype.insertBefore = insertBefore
    Object.defineProperty(Node.prototype, "textContent", textContent)
    Object.defineProperty(Element.prototype, "innerHTML", innerHTML)
  }
  return seen
}

/** Each source exercised the way the applications use it, in a clean document. */
const EXERCISE: Record<StyleSource, () => Promise<void>> = {
  // sonner inserts its stylesheet when the module is evaluated (__insertCSS at
  // the top level of dist/index.mjs): an empty element first, then its text.
  async sonner() {
    await import("sonner")
  },
  async "radix-scroll-area"() {
    const { ScrollArea } = await import("@authsystem/ui/scroll-area")
    await act(async () => {
      render(<ScrollArea>content</ScrollArea>)
    })
  },
  async "radix-select"() {
    const { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } = await import(
      "@authsystem/ui/select"
    )
    await act(async () => {
      render(
        <Select open value="a">
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="a">A</SelectItem>
          </SelectContent>
        </Select>
      )
    })
  },
  // input-otp adds an EMPTY <style id="input-otp-style"> and writes its rules
  // through the element's sheet (insertRule), which CSP does not check.
  async "input-otp"() {
    const { InputOTP, InputOTPGroup, InputOTPSlot } = await import("@authsystem/ui/input-otp")
    await act(async () => {
      render(
        <InputOTP maxLength={2}>
          <InputOTPGroup>
            <InputOTPSlot index={0} />
            <InputOTPSlot index={1} />
          </InputOTPGroup>
        </InputOTP>
      )
    })
  },
}

export interface InlineStyle {
  /** The CSP hash value, without the quotes the header puts around it. */
  hash: string
  /** Which injectors produce this exact text. */
  sources: StyleSource[]
  text: string
}

/**
 * Runs every source once and returns, per source, the distinct texts it produced.
 *
 * The document gets adoptedStyleSheets for the run, as every browser the card
 * supports has: jsdom lacks it, and without it the patched libraries and
 * use-adopted-style-sheet.ts fall back to the <style> a browser never sees
 * (the dialog scroll lock under an open Select would be recorded here).
 */
export async function collectBySource(): Promise<Record<StyleSource, string[]>> {
  const result = {} as Record<StyleSource, string[]>
  const hadAdoption = "adoptedStyleSheets" in document
  if (!hadAdoption) {
    Object.defineProperty(document, "adoptedStyleSheets", { configurable: true, writable: true, value: [] })
  }
  try {
    for (const source of STYLE_SOURCES) {
      document.head.innerHTML = ""
      const texts = await recordInlineStyles(EXERCISE[source])
      cleanup()
      result[source] = [...new Set(texts)]
    }
  } finally {
    if (!hadAdoption) delete (document as { adoptedStyleSheets?: unknown }).adoptedStyleSheets
  }
  return result
}

/** The inline-styles.json content for one application, sorted so its diff reads. */
export function inventoryFor(app: AppName, bySource: Record<StyleSource, string[]>): InlineStyle[] {
  const byText = new Map<string, Set<StyleSource>>()
  for (const source of APP_STYLE_SOURCES[app]) {
    for (const text of bySource[source]) {
      byText.set(text, (byText.get(text) ?? new Set()).add(source))
    }
  }
  return [...byText]
    .map(([text, sources]) => ({ hash: cspHash(text), sources: [...sources].sort(), text }))
    .sort((a, b) => a.sources[0].localeCompare(b.sources[0]) || a.text.length - b.text.length)
}

/** The file as `pnpm csp:collect` writes it. */
export function inventoryFile(app: AppName, styles: InlineStyle[]) {
  return `${JSON.stringify(
    {
      $comment: [
        `The inline <style> texts that reach the ${app} bundle, and their CSP hashes.`,
        "GENERATED by `pnpm csp:collect` from the installed libraries",
        "(packages/ui/src/csp/collect-inline-styles.tsx). Do not edit by hand:",
        "inline-style-inventory.test.ts fails when this file and the collector disagree.",
      ],
      app,
      styles,
    },
    null,
    2
  )}\n`
}
