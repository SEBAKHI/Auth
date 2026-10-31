import * as React from "react"

/**
 * The client's shape check for an email address. It only spares a round trip:
 * the server's own rule decides, and its answer still reaches the form.
 */
export const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * When an email field says its address is malformed: never while it is being
 * typed. Every address is "invalid" until its last characters arrive, so a
 * verdict on each keystroke reads as a scolding for an address that is not
 * finished. The verdict appears when the person leaves a field they typed in,
 * or tries to submit, and it goes away on their next edit.
 *
 * The submit button stays enabled while the address is merely unchecked:
 * `check()` runs on submit, shows the verdict and says whether to send.
 */
export function useEmailFieldCheck(value: string) {
  const [shown, setShown] = React.useState(false)
  const valid = EMAIL_RE.test(value)

  return {
    valid,
    /** Drives `data-invalid`, `aria-invalid` and the field's error. */
    invalid: shown && !valid,
    /** Leaving an empty field is not an attempt; leaving a typed one is. */
    onBlur: () => {
      if (value.length > 0) setShown(true)
    },
    onEdit: () => setShown(false),
    /** On submit: reveal the verdict, and send only when it passes. */
    check: () => {
      setShown(true)
      return valid
    },
  }
}
