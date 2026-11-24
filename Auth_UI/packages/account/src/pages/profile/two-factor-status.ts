import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { toNumber, unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

/**
 * At or below this many recovery codes the security tab warns, with the way to a
 * new set (AM-S08-1, the owner's decision of 2026-09-28): an account down to its
 * last code with the phone gone has no way back but an administrator.
 */
export const LOW_RECOVERY_CODES = 3

/**
 * The signed-in account's two-factor status, under "me", so every change to the
 * account's two-factor refreshes it too. Read only where it says something: while
 * the app is on (the codes left), or for an account that administers the platform
 * (the warnings of `AccessRecoveryWarnings`). `permissions` is the account's
 * platform authority, the server's own reading of it.
 */
export function useTwoFactorStatus(
  me: Schemas["UserDto"],
  appEnabled: boolean
): Schemas["TwoFactorStatusResponse"] | undefined {
  const administers = (me.permissions?.length ?? 0) > 0
  const query = useQuery({
    queryKey: ["me", "two-factor-status"],
    enabled: appEnabled || administers,
    queryFn: () => unwrap(api.GET("/api/v1/auth/2fa/status")),
  })
  return query.data
}

/**
 * How many recovery codes the signed-in account has left: a number while a
 * factor is on, null without one.
 */
export function useRecoveryCodesRemaining(
  me: Schemas["UserDto"],
  enabled: boolean
): number | null {
  const remaining = useTwoFactorStatus(me, enabled)?.recoveryCodesRemaining
  return enabled && remaining !== null && remaining !== undefined
    ? toNumber(remaining)
    : null
}
