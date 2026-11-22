import { Ellipsis, UserPlus, X } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Avatar, AvatarFallback } from "@authsystem/ui/avatar"
import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { initials } from "@authsystem/ui/initials"
import { Item } from "@authsystem/ui/item"
import { Spinner } from "@authsystem/ui/spinner"

import type { RememberedAccount } from "./remembered-accounts"

/**
 * The sign-in page's "Pick an account" list (owner, 2026-11-22, reversing
 * D-215-4), as Microsoft's sign-in draws it: one row per account this browser
 * signed in with, its name over its address; a "⋯" button at the row's end whose
 * menu removes the account from the list (Q2 (a): two presses, so a stray tap on
 * a phone removes nothing); and "Use a different account" under them.
 *
 * Pressing a row is the page's own "Continue" for that address: the page does
 * the rest. While it does, that row shows the wait and the other controls wait
 * with it, so one press sends one question and at most one code.
 */
export function AccountPicker({
  accounts,
  picking,
  onPick,
  onRemove,
  onUseAnother,
}: {
  /** Newest first, as remembered-accounts.ts keeps them. */
  accounts: readonly RememberedAccount[]
  /** The account whose press is on its way, if any. */
  picking: string | null
  onPick: (account: RememberedAccount) => void
  onRemove: (account: RememberedAccount) => void
  onUseAnother: () => void
}) {
  const { t } = useTranslation()
  const listRef = React.useRef<HTMLUListElement>(null)
  // A row's menu removed it: the menu's trigger went with the row, so the focus
  // moves to the first account left instead of falling to the page's start.
  // The page shows the address field, which takes the focus, once none is left.
  const removedRef = React.useRef(false)
  React.useEffect(() => {
    if (!removedRef.current) return
    removedRef.current = false
    listRef.current
      ?.querySelector<HTMLButtonElement>("button[data-account]")
      ?.focus()
  }, [accounts])

  const busy = picking !== null

  return (
    <div className="flex flex-col gap-1">
      <ul
        ref={listRef}
        aria-label={t("auth.accounts.listLabel")}
        className="flex flex-col gap-1"
        data-testid="account-list"
      >
        {accounts.map((account, index) => {
          const title = account.name ?? account.email
          return (
            // One bordered row holds the account and its menu button, so the
            // "⋯" reads as this account's; each keeps its own highlight, since
            // the account's says "press to pick it", which the "⋯" is not
            // (owner, 2026-11-22: tried in place of Microsoft's one highlight).
            <Item
              key={account.id}
              asChild
              variant="outline"
              size="xs"
              className="flex-nowrap items-stretch gap-1 p-1"
            >
              <li>
                <Button
                  type="button"
                  variant="ghost"
                  // The row's height follows its two lines; its contents start
                  // where reading starts.
                  className="h-auto min-w-0 flex-1 justify-start gap-3 py-2"
                  // The other rows wait while one is on its way; the one pressed
                  // stays as it is, showing the wait, and its second press is
                  // the page's to ignore.
                  disabled={busy && picking !== account.id}
                  aria-busy={picking === account.id}
                  // The list takes the focus as the address field did.
                  autoFocus={index === 0}
                  data-account={account.id}
                  onClick={() => onPick(account)}
                >
                  <Avatar size="lg" aria-hidden="true">
                    <AvatarFallback>{initials(title)}</AvatarFallback>
                  </Avatar>
                  {/* Each line as wide as its text, never wider than the row,
                    and in its own direction: a Latin address on a
                    right-to-left page sits at the row's start and, cut short,
                    loses its end rather than its first letters. */}
                  <span className="flex min-w-0 flex-1 flex-col items-start">
                    <span dir="auto" className="max-w-full truncate">
                      {title}
                    </span>
                    {account.name ? (
                      <span
                        dir="auto"
                        className="max-w-full truncate font-normal text-muted-foreground"
                      >
                        {account.email}
                      </span>
                    ) : null}
                  </span>
                  {picking === account.id ? (
                    <Spinner data-icon="inline-end" />
                  ) : null}
                </Button>
                {/* Not modal: the row, and the menu with it, unmounts in the
                  same render that removes the account, and a modal menu
                  would leave the page's pointer lock to that unmount. */}
                <DropdownMenu modal={false}>
                  <DropdownMenuTrigger asChild>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      // As tall as the row: a finger finds it anywhere on the
                      // row's end, not only on the three dots.
                      className="h-auto"
                      disabled={busy}
                      aria-label={t("auth.accounts.moreOptions", {
                        account: account.email,
                      })}
                      data-testid="account-options"
                    >
                      <Ellipsis />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent
                    align="end"
                    onCloseAutoFocus={(event) => {
                      // The trigger is gone with its row: the effect above
                      // places the focus.
                      if (removedRef.current) event.preventDefault()
                    }}
                  >
                    <DropdownMenuGroup>
                      <DropdownMenuItem
                        onSelect={() => {
                          removedRef.current = true
                          onRemove(account)
                        }}
                      >
                        <X />
                        {t("auth.accounts.remove")}
                      </DropdownMenuItem>
                    </DropdownMenuGroup>
                  </DropdownMenuContent>
                </DropdownMenu>
              </li>
            </Item>
          )
        })}
      </ul>
      {/* Not an account, so no border; set in the same row box, so its circle
          lines up with the accounts' circles. */}
      <Item size="xs" className="flex-nowrap gap-1 p-1">
        <Button
          type="button"
          variant="ghost"
          className="h-auto min-w-0 flex-1 justify-start gap-3 py-2"
          disabled={busy}
          onClick={onUseAnother}
          data-testid="use-another-account"
        >
          <Avatar size="lg" aria-hidden="true">
            <AvatarFallback>
              <UserPlus />
            </AvatarFallback>
          </Avatar>
          {t("auth.useDifferentAccount")}
        </Button>
      </Item>
    </div>
  )
}
