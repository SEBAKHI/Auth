import { ACCOUNTS_URL } from "@authsystem/api/env"

/**
 * The two lines a linked application adds to its header to show IAM's account bar: the
 * grid button and the person's avatar, as the accounts app draws them (owner decisions of
 * 2026-11-02). The accounts address is this deployment's (`VITE_ACCOUNTS_URL`). The
 * sign-out address is a placeholder the developer replaces with the application's own,
 * which ends its session and then IAM's.
 */
export function accountBarSnippet(accountsUrl: string = ACCOUNTS_URL): string {
  return [
    `<script async src="${accountsUrl}/embed/v1/account-bar.js"></script>`,
    `<iam-account-bar signout-href="/sign-out"></iam-account-bar>`,
  ].join("\n")
}
