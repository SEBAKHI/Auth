import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"

/**
 * Answers after which a picture of two-factor is out of date: another tab or
 * device switched it on or off, or replaced the secret a setup showed.
 */
export const STALE_TWO_FACTOR_CODES: readonly PublishedErrorCode[] = [
  "User.TwoFactorAlreadyEnabled",
  "User.TwoFactorNotEnabled",
  "TwoFactor.SetupRequired",
]
