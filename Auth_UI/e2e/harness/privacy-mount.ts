import { existsSync } from "node:fs"

import { assertServableTree } from "./static-host"

/**
 * /privacy on the accounts host, mapped to a folder the test provides - the way
 * production maps it to a Plesk/IIS virtual directory on persistent storage
 * (Auth_UI/README.md, "Privacy policy virtual directory"). That virtual
 * directory is NOT its own application, so its files are served with the
 * accounts site's web.config: its rewrite rules, its headers, its CSP.
 *
 * S30a's own tests mount synthetic files, which prove the rewrites and headers
 * and nothing about the documents. A check of CSP on /privacy (S02, E6) must
 * mount a document PolicyDocumentRenderer actually produced, or it cannot fail
 * the way the defect shows.
 */
export class PrivacyMount {
  static readonly PREFIX = "/privacy"
  #dir: string | null = null

  /** Serves `dir` at /privacy until the test ends. */
  mount(dir: string) {
    if (!existsSync(dir)) throw new Error(`privacy mount: ${dir} does not exist`)
    assertServableTree(dir)
    this.#dir = dir
  }

  clear() {
    this.#dir = null
  }

  /** The folder and the path inside it for a URL path under /privacy, or null. */
  map(urlPath: string): { base: string; relative: string } | null {
    const lower = urlPath.toLowerCase()
    if (lower !== PrivacyMount.PREFIX && !lower.startsWith(`${PrivacyMount.PREFIX}/`)) return null
    if (!this.#dir) return { base: "", relative: "" }
    return { base: this.#dir, relative: urlPath.slice(PrivacyMount.PREFIX.length) }
  }
}
