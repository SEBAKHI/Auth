import type * as React from "react"

import { OtpInput } from "@authsystem/ui/common/otp-input"
import { Field, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"

/**
 * The labelled field a second-step code is typed into (OI-243): six boxes for every
 * six-digit code — from the authenticator app or from the email — and a plain field
 * for a recovery code, which is longer, not numeric, and transcribed exactly. One
 * field, so no screen asks for six digits in a free-text box again.
 *
 * Anything passed as children (a description, a hint) sits under the control,
 * inside the same `Field`.
 */
export function OneTimeCodeField({
  id,
  label,
  value,
  onChange,
  recovery = false,
  onComplete,
  disabled,
  autoFocus,
  autoComplete,
  children,
}: {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  /** A recovery code instead of a six-digit one. */
  recovery?: boolean
  /** Called with the sixth digit; only where nothing else is left to fill. */
  onComplete?: (value: string) => void
  disabled?: boolean
  autoFocus?: boolean
  /**
   * The six-digit field's browser hint, "one-time-code" when not given. A screen
   * with two code fields keeps it on the authenticator's alone: a password manager
   * fills the first field that carries it.
   */
  autoComplete?: string
  children?: React.ReactNode
}) {
  return (
    <Field data-disabled={disabled || undefined}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      {recovery ? (
        // Pinned LTR like the sign-in page's recovery field: a code is copied
        // character by character and must not follow an RTL page.
        <Input
          id={id}
          dir="ltr"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          autoComplete="off"
          disabled={disabled}
          autoFocus={autoFocus}
          className="font-mono"
        />
      ) : (
        <OtpInput
          id={id}
          label={label}
          value={value}
          onChange={onChange}
          onComplete={onComplete}
          disabled={disabled}
          autoFocus={autoFocus}
          autoComplete={autoComplete}
        />
      )}
      {children}
    </Field>
  )
}
