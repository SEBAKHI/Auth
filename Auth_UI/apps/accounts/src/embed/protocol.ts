/**
 * The account bar of linked applications (owner decisions of 2026-11-02): what its frames
 * and the loader a linked application adds (`public/embed/v1/account-bar.js`) say to each
 * other.
 *
 * Nothing about the person crosses: no name, no email, no application. Only requests to
 * open, close, resize, sign out or sign in, and the loader's answers about where a panel
 * sits. The frames read the person themselves, from the API, by the single sign-on cookie.
 *
 * Both sides check who they talk to. The loader accepts messages only from the accounts
 * origin, and only from the frames it made. A frame accepts messages only from its own
 * parent, on an https subdomain of the IAM site (`isLinkedAppOrigin`), and posts only to
 * that origin. The CSP's frame-ancestors (web.config) is the browser's own fence around the
 * same rule.
 *
 * The loader is plain JavaScript served at a fixed address, so it cannot import this file.
 * `account-bar-loader.test.ts` holds the two to the same names.
 */

/** Every message carries it: a page may host other frames that post their own messages. */
export const NAMESPACE = "iam-account-bar"

export const PANELS = ["apps", "account"] as const
export type Panel = (typeof PANELS)[number]

export const HIDE_VALUES = ["apps", "avatar"] as const
export type Hidden = (typeof HIDE_VALUES)[number]

export const THEMES = ["light", "dark"] as const
export type HostTheme = (typeof THEMES)[number]

/** What a frame asks of the loader. */
export type FrameMessage =
  /** `inset`: how far inside the frame's edge what it draws begins. */
  | { ns: typeof NAMESPACE; type: "size"; width: number; height: number; inset: number }
  | { ns: typeof NAMESPACE; type: "toggle"; panel: Panel }
  | { ns: typeof NAMESPACE; type: "close"; focusBar: boolean }
  | { ns: typeof NAMESPACE; type: "signout" }
  | { ns: typeof NAMESPACE; type: "signin" }

/** What the loader tells a frame. */
export type HostMessage =
  /** To the bar: which panel is open, and whether its button takes the focus back. */
  | { ns: typeof NAMESPACE; type: "state"; open: Panel | null; focus: boolean }
  /** To a panel: it is shown, with the height it may take. */
  | { ns: typeof NAMESPACE; type: "opened"; maxHeight: number }
  /** To every frame: the application's colour mode changed (its `theme` attribute). */
  | { ns: typeof NAMESPACE; type: "theme"; value: HostTheme | null }

/** The IAM site, the registrable domain linked applications live under (deploy-time value). */
export const IAM_SITE: string = (import.meta.env.VITE_IAM_SITE ?? "localhost").trim().toLowerCase()

/**
 * Whether `origin` may frame the bar: https, on a subdomain of `site` (never the site
 * itself, which serves no application here). Local development (`site` "localhost")
 * accepts any port of https://localhost.
 */
export function isLinkedAppOrigin(origin: string, site: string = IAM_SITE): boolean {
  let url: URL
  try {
    url = new URL(origin)
  } catch {
    return false
  }
  if (url.protocol !== "https:" || url.origin !== origin) return false
  const host = url.hostname.toLowerCase()
  if (site === "localhost") return host === "localhost"
  return host.endsWith(`.${site}`) && host.length > site.length + 1
}

/**
 * The parent this frame talks to: the `origin` the loader wrote into the frame's address,
 * when it is a linked application's and, where the browser can say who really framed us
 * (`location.ancestorOrigins`, Chromium and WebKit), when it is that parent. Null otherwise:
 * the frame then draws nothing and posts nothing.
 */
export function readParentOrigin(location: Location = window.location): string | null {
  if (window.parent === window) return null
  const claimed = new URLSearchParams(location.search).get("origin")
  if (!claimed || !isLinkedAppOrigin(claimed)) return null
  const ancestors = location.ancestorOrigins
  if (ancestors && ancestors.length > 0 && ancestors[0] !== claimed) return null
  return claimed
}

type WithoutNamespace<T> = T extends unknown ? Omit<T, "ns"> : never

/** Posts to the parent, to its origin only. */
export function tellHost(parentOrigin: string, message: WithoutNamespace<FrameMessage>) {
  window.parent.postMessage({ ns: NAMESPACE, ...message }, parentOrigin)
}

/** Listens to the parent's messages, and to nobody else's. */
export function onHostMessage(parentOrigin: string, handle: (message: HostMessage) => void): () => void {
  const listener = (event: MessageEvent) => {
    if (event.source !== window.parent || event.origin !== parentOrigin) return
    const data = event.data as Partial<HostMessage> | null
    if (!data || typeof data !== "object" || data.ns !== NAMESPACE) return
    handle(data as HostMessage)
  }
  window.addEventListener("message", listener)
  return () => window.removeEventListener("message", listener)
}

/** One of `values`, or null. */
export function oneOf<T extends string>(values: readonly T[], raw: string | null): T | null {
  return raw !== null && (values as readonly string[]).includes(raw) ? (raw as T) : null
}
