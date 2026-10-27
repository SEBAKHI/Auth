import { describe, expect, it } from "vitest"

/**
 * PasswordServerErrorCallSitesTests (S09, T7). The server's verdict on a new
 * password (a broken rule, a breached or recently used password) reaches the
 * field only through `applyPasswordServerErrors`. A form that renders a
 * new-password field and skips it shows that verdict in a toast that vanishes
 * while the person is typing another password, and no runtime assertion sees
 * the difference. So the source is the test: every form that renders
 * `<PasswordField` must call it, including the seventh one someone adds later.
 */
const sources = import.meta.glob(
  [
    "../../../apps/*/src/**/*.tsx",
    "../../*/src/**/*.tsx",
    "!../../../**/*.test.tsx",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

// The six forms that set a new password when S09 was written (the sign-up's
// moved into the shared code flow with OI-215).
const KNOWN_FORMS = [
  "packages/auth/src/pages/email-code-flow.tsx",
  "apps/console/src/pages/users/user-form-dialog.tsx",
  "packages/account/src/pages/profile/profile-security.tsx",
  "packages/auth/src/pages/accept-invitation.tsx",
  "packages/auth/src/pages/force-password-change.tsx",
  "packages/auth/src/pages/reset-password.tsx",
]

// Glob keys are relative to this file; name each form from the workspace root.
// (A literal `new URL("…", import.meta.url)` would be rewritten by Vite as an
// asset import, so the root is cut from this file's own URL instead.)
const HERE = import.meta.url
const WORKSPACE_ROOT = HERE.slice(
  0,
  HERE.lastIndexOf("/packages/auth/src/") + 1
)

const passwordForms = Object.entries(sources)
  .filter(([, source]) => /<PasswordField\b/.test(source))
  .map(([key, source]) => ({
    path: new URL(key, HERE).href.slice(WORKSPACE_ROOT.length),
    source,
  }))

describe("every new-password form puts the server's verdict under the field", () => {
  it("finds the forms at all", () => {
    // A glob or a pattern that stopped matching would leave the check below
    // with nothing to check, and green.
    expect(Object.keys(sources).length).toBeGreaterThan(100)
    expect(passwordForms.length).toBeGreaterThanOrEqual(KNOWN_FORMS.length)
    expect(passwordForms.map((form) => form.path)).toEqual(
      expect.arrayContaining(KNOWN_FORMS)
    )
  })

  it.each(KNOWN_FORMS)("%s calls applyPasswordServerErrors", (path) => {
    const form = passwordForms.find((candidate) => candidate.path === path)
    expect(form?.source).toMatch(/\bapplyPasswordServerErrors\(/)
  })

  it("no form that renders a PasswordField skips it", () => {
    const skipping = passwordForms
      .filter((form) => !/\bapplyPasswordServerErrors\(/.test(form.source))
      .map((form) => form.path)
    expect(skipping).toEqual([])
  })
})
