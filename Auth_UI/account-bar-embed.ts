/**
 * The account bar of linked applications (owner decisions of 2026-11-02): the accounts app
 * serves its two frames as pages of their own beside index.html, under /embed/v1/, where
 * web.config gives them a policy of their own (frame-ancestors on the IAM site's
 * subdomains). The loader a linked application adds, /embed/v1/account-bar.js, is a plain
 * file in public/: its address must never change, so it is not a hashed bundle.
 *
 * The console has no account bar.
 */

/** The frames' pages, relative to the app's root, as they are served. */
export const ACCOUNT_BAR_PAGES = ["embed/v1/bar.html", "embed/v1/panel.html"] as const

/** The build inputs of the bar's frames. */
export function accountBarInputs(appDir: string): Record<string, string> {
  return {
    "account-bar": `${appDir}/${ACCOUNT_BAR_PAGES[0]}`,
    "account-bar-panel": `${appDir}/${ACCOUNT_BAR_PAGES[1]}`,
  }
}
