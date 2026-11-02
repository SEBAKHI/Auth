import { describe, expect, it } from "vitest"

import {
  accountBarCsp,
  accountBarSnippet,
  DEFAULT_ACCOUNT_BAR_CHOICES,
  endSessionUrl,
  IAM_SIGN_IN,
  signInChoices,
} from "./account-bar-snippet"

const ACCOUNTS = "https://accounts.contoso-sample.net"

/** The code Platform settings › Account bar writes for a linked application. */
describe("accountBarSnippet", () => {
  it("writes the loader and the element with the application's sign-out address by default", () => {
    expect(accountBarSnippet(DEFAULT_ACCOUNT_BAR_CHOICES, ACCOUNTS)).toBe(
      `<script async src="${ACCOUNTS}/embed/v1/account-bar.js"></script>\n` +
        `<iam-account-bar signout-href="/sign-out"></iam-account-bar>`
    )
  })

  it.each([
    ["both", null],
    ["apps", 'hide="avatar"'],
    ["avatar", 'hide="apps"'],
  ] as const)("shows %s with %s", (shows, attribute) => {
    const element = accountBarSnippet({ ...DEFAULT_ACCOUNT_BAR_CHOICES, shows }, ACCOUNTS).split("\n")[1]
    if (attribute) expect(element).toContain(attribute)
    else expect(element).not.toContain("hide=")
  })

  it("names the application's colour mode, and none for the person's own choice", () => {
    expect(accountBarSnippet({ ...DEFAULT_ACCOUNT_BAR_CHOICES, theme: "dark" }, ACCOUNTS)).toContain('theme="dark"')
    expect(accountBarSnippet(DEFAULT_ACCOUNT_BAR_CHOICES, ACCOUNTS)).not.toContain("theme=")
  })

  it("adds the sign-in address only when given, and leaves out an empty sign-out address", () => {
    const snippet = accountBarSnippet(
      { ...DEFAULT_ACCOUNT_BAR_CHOICES, signOutHref: "  ", signInHref: " /login " },
      ACCOUNTS
    )
    expect(snippet).toContain('<iam-account-bar signin-href="/login"></iam-account-bar>')
    expect(snippet).not.toContain("signout-href")
  })

  it("keeps a typed address inside its attribute", () => {
    const element = accountBarSnippet(
      { ...DEFAULT_ACCOUNT_BAR_CHOICES, signOutHref: '/out?a=1&b="2"><script>' },
      ACCOUNTS
    ).split("\n")[1]
    expect(element).toBe(
      '<iam-account-bar signout-href="/out?a=1&amp;b=&quot;2&quot;>&lt;script>"></iam-account-bar>'
    )
  })
})

describe("endSessionUrl", () => {
  it("names the application and its return address, encoded", () => {
    expect(endSessionUrl("CMS", "https://cms.contoso-sample.net/signed-out?x=1", "https://auth.contoso-sample.net")).toBe(
      "https://auth.contoso-sample.net/api/v1/auth/end-session?client_id=CMS&post_logout_redirect_uri=" +
        "https%3A%2F%2Fcms.contoso-sample.net%2Fsigned-out%3Fx%3D1"
    )
  })
})

describe("accountBarCsp", () => {
  it("allows the accounts app for the loader and its frames", () => {
    expect(accountBarCsp(ACCOUNTS)).toBe(`script-src ${ACCOUNTS}\nframe-src ${ACCOUNTS}`)
  })
})

describe("signInChoices", () => {
  it("offers the registered sign-in start, then the base address, then IAM's page, once each", () => {
    expect(
      signInChoices({ initiateLoginUri: "https://crm.example.com/signin", baseUrl: "https://crm.example.com/" })
    ).toEqual(["https://crm.example.com/signin", "https://crm.example.com/", IAM_SIGN_IN])
    expect(signInChoices({ initiateLoginUri: " https://a.example.com/ ", baseUrl: "https://a.example.com/" })).toEqual([
      "https://a.example.com/",
      IAM_SIGN_IN,
    ])
    expect(signInChoices({ initiateLoginUri: null, baseUrl: null })).toEqual([IAM_SIGN_IN])
  })
})
