import { ChevronDown } from "lucide-react"
import * as React from "react"

import { Button } from "@authsystem/ui/button"

/**
 * The address a later step of a sign-in stands on, as the large identity
 * providers show it: a button that goes back to change it, never a field.
 *
 * It replaced a read-only email field, which looked typeable and was not: a
 * person who wanted another account typed into it, saw nothing happen and took
 * the page for broken. The way back is now the address itself.
 *
 * Its one icon is a down chevron after the address, as Google's sign-in shows
 * it (owner, 2026-11-23), now that a sign-in page can list the accounts this
 * browser signed in with: pressing it goes back to where the address was
 * chosen, that list where there is one. Google's own chip is a link to its
 * account chooser, not a menu (its page, read 2026-11-23), so this one opens no
 * menu either and claims none (no `aria-haspopup`). It replaced Microsoft's
 * back arrow, which the owner first chose (PR 171) while there was no list to
 * go back to. A pencil would promise editing in place.
 *
 * Password managers still pair the address with the password: the hidden
 * `username` input beside the button carries it, inside the same form, as
 * Chromium's guidance for a sign-in split over two steps asks. It is hidden
 * visually only (`sr-only`), never `display: none` or `type="hidden"`, which
 * some managers skip; read-only, so a manager's fill cannot change the address
 * the form sends; and out of the tab order and the accessibility tree, since
 * the button already names the address.
 */
export function AddressChip({
  email,
  label,
  onChange,
}: {
  /** The address the step stands on, the one the form sends. */
  email: string
  /** What the button does, read after the address by a screen reader. */
  label: string
  /** Back to where the address was chosen: the account list, or the field. */
  onChange: () => void
}) {
  const descriptionId = React.useId()
  return (
    <>
      {/* Named by the address it shows (WCAG 2.5.3, label in name), and
          described by what it does: a screen reader says the address, then
          "button", then the way back. */}
      <Button
        type="button"
        variant="outline"
        size="sm"
        // Centered under the title, as the step's subject; never wider than
        // the form, so a long address is cut short instead of the page.
        className="max-w-full self-center"
        title={email}
        aria-describedby={descriptionId}
        onClick={onChange}
        data-testid="address-chip"
      >
        {/* `auto`, not a forced direction: the address's own first letter
            decides, so on a right-to-left page a long Latin address keeps its
            start (the person's name) and loses its end, not the reverse. */}
        <span dir="auto" className="min-w-0 truncate">
          {email}
        </span>
        {/* After the address in either direction: a down chevron has no side
            to point to, so it never turns. */}
        <ChevronDown data-icon="inline-end" />
      </Button>
      <span id={descriptionId} className="sr-only">
        {label}
      </span>
      <input
        type="email"
        name="email"
        autoComplete="username"
        value={email}
        readOnly
        tabIndex={-1}
        aria-hidden="true"
        className="sr-only"
        data-testid="address-chip-username"
      />
    </>
  )
}
