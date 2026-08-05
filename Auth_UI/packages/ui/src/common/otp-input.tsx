import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@authsystem/ui/input-otp"

/** Digits in an issued code — mirrors the server's OTP generator. */
export const OTP_CODE_LENGTH = 6

/**
 * The numeric one-time-code field shared by every OTP surface — email
 * verification and both account-deletion entry points. The slot count is the
 * single source of truth for "how long is a code", so a caller can never drift
 * from the server's 6 digits by hand-rolling the slot loop again.
 */
export function OtpInput({
  value,
  onChange,
  onComplete,
  label,
  length = OTP_CODE_LENGTH,
  disabled,
  autoFocus,
}: {
  value: string
  onChange: (value: string) => void
  onComplete?: (value: string) => void
  /** Accessible name — the field has no visible label anywhere it is used. */
  label: string
  length?: number
  disabled?: boolean
  autoFocus?: boolean
}) {
  return (
    <InputOTP
      // A code is entered and read left-to-right whatever the page direction.
      dir="ltr"
      maxLength={length}
      pattern={REGEXP_ONLY_DIGITS}
      value={value}
      onChange={onChange}
      onComplete={onComplete}
      disabled={disabled}
      autoFocus={autoFocus}
      aria-label={label}
    >
      <InputOTPGroup>
        {Array.from({ length }).map((_, index) => (
          <InputOTPSlot key={index} index={index} />
        ))}
      </InputOTPGroup>
    </InputOTP>
  )
}

/** Codes are issued against a per-address server rate limit; throttle locally too. */
export const RESEND_COOLDOWN_MS = 60_000
