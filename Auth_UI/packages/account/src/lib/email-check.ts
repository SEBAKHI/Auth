import * as React from "react"

/**
 * The client's shape check for an email address. It only spares a round trip:
 * the server's own rule decides, and its answer still reaches the form.
 */
export const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * An email field outside a form library, timed like shadcn's forms, that is
 * like react-hook-form's defaults (`mode: "onSubmit"`, `reValidateMode:
 * "onChange"`): nothing is judged while typing, or on leaving the field, until
 * the first submit attempt. From then on the verdict is live: the error stays
 * while the address is malformed and goes the moment it is well-formed.
 *
 * The submit button stays enabled while the address is unchecked: `check()`
 * runs on submit, starts the live verdict and says whether to send.
 */
export function useEmailFieldCheck(value: string) {
  const [attempted, setAttempted] = React.useState(false)
  const valid = EMAIL_RE.test(value)

  return {
    valid,
    /** Drives `data-invalid`, `aria-invalid` and the field's error. */
    invalid: attempted && !valid,
    /** On submit: start judging, and send only when the address passes. */
    check: () => {
      setAttempted(true)
      return valid
    },
  }
}
