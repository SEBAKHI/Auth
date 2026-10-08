import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { toNumber, unwrap } from "@authsystem/api/helpers"

/**
 * At or below this many recovery codes the security tab warns, with the way to a
 * new set (AM-S08-1, the owner's decision of 2026-09-28): an account down to its
 * last code with the phone gone has no way back but an administrator.
 */
export const LOW_RECOVERY_CODES = 3

/**
 * How many recovery codes the signed-in account has left: a number while a
 * factor is on, null without one. Read only while the factor is on, and under
 * "me", so every change to the account's two-factor refreshes it too.
 */
export function useRecoveryCodesRemaining(enabled: boolean): number | null {
  const query = useQuery({
    queryKey: ["me", "two-factor-status"],
    enabled,
    queryFn: () => unwrap(api.GET("/api/v1/auth/2fa/status")),
  })

  const remaining = query.data?.recoveryCodesRemaining
  return enabled && remaining !== null && remaining !== undefined
    ? toNumber(remaining)
    : null
}
