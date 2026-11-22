import { ChevronDown, CircleUserRound } from "lucide-react"
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
  /** Back to the step where the address is typed. */
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
        <CircleUserRound data-icon="inline-start" />
        {/* `auto`, not a forced direction: the address's own first letter
            decides, so on a right-to-left page a long Latin address keeps its
            start (the person's name) and loses its end, not the reverse. */}
        <span dir="auto" className="min-w-0 truncate">
          {email}
        </span>
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
