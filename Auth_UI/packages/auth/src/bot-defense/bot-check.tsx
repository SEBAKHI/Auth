import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"

import { ensureBotDefenseConfig, useBotDefenseConfig } from "@authsystem/api/bot-defense"

import { BotCheckField } from "./bot-check-field"
import { TokenSlot, type BotAction, type CheckState } from "./token-slot"

export type { BotAction } from "./token-slot"

/** Whether a submit goes out, and with which token. */
export type BotTokenResult = { proceed: true; token?: string } | { proceed: false }

/**
 * How long an enforced submit waits for the widget's token: long enough for an
 * interactive check to be ticked, short enough that a stuck widget ends in a
 * sentence rather than a spinner.
 */
export const ENFORCED_WAIT_MS = 15_000

export interface BotCheck {
  /**
   * The check's field: the widget, and its sentences when it needs the person.
   * Render it inside the form's FieldGroup, before the submit. Keyed, so a page
   * whose steps share one FieldGroup keeps one widget across them.
   */
  field: React.ReactNode
  /**
   * A token for `action`. Off, or no sitekey: proceed with none. Monitor:
   * proceed with the ready token, or none, never waiting. Enforce: wait for the
   * token; with none in time, do not proceed, and the field says why.
   */
  prepare: (action: BotAction) => Promise<BotTokenResult>
  /**
   * The token taken by `prepare` is used up: it went out with a request
   * (accepted or not), or the request it was taken for will not go out. The
   * widget makes a fresh one.
   */
  spent: () => void
}

/**
 * The bot check of a sign-in page (S13, BD9): Cloudflare Turnstile, rendered
 * only when the server names a sitekey, in its managed mode with
 * `appearance: "interaction-only"`, so most people see nothing at all and the
 * box shows only when Cloudflare asks for a click (D-S13-2).
 *
 * One widget per page. Its action follows the request it serves: `expected` is
 * the page's next request, and `prepare` for another action renders the same
 * widget again with that one (the sign-in page sends both the password sign-in
 * and the code start, D4).
 */
export function useBotCheck(expected: BotAction): BotCheck {
  const queryClient = useQueryClient()
  const { config, isError, refetch } = useBotDefenseConfig()
  const [override, setOverride] = React.useState<BotAction | null>(null)
  const action = override ?? expected
  const [state, setState] = React.useState<CheckState>({ kind: "quiet" })
  const [generation, setGeneration] = React.useState(0)
  const [slot] = React.useState(() => new TokenSlot(action))

  const prepare = React.useCallback(
    async (needed: BotAction): Promise<BotTokenResult> => {
      const current = await ensureBotDefenseConfig(queryClient)
      if (!current || current.mode === "Off" || current.siteKey === null) return { proceed: true }

      if (slot.action !== needed) {
        // The widget is rendered again for this request's action.
        setOverride(needed === expected ? null : needed)
      }
      const ready = slot.action === needed ? slot.take(needed) : null
      if (ready) return { proceed: true, token: ready }
      if (current.mode !== "Enforce") return { proceed: true }

      setState((previous) => (previous.kind === "incomplete" ? { kind: "quiet" } : previous))
      const token = await slot.wait(needed, ENFORCED_WAIT_MS)
      if (token) return { proceed: true, token }
      setState((previous) => (previous.kind === "quiet" ? { kind: "incomplete" } : previous))
      return { proceed: false }
    },
    [queryClient, slot, expected]
  )

  const spent = React.useCallback(() => {
    setOverride(null)
    slot.renew()
  }, [slot])

  const siteKey = config?.siteKey ?? null
  const field =
    siteKey === null && !isError ? null : (
      <BotCheckField
        key="bot-check"
        siteKey={siteKey}
        action={action}
        generation={generation}
        slot={slot}
        state={state}
        onState={setState}
        configFailed={isError}
        onRetry={() => {
          setState({ kind: "quiet" })
          if (isError) refetch()
          setGeneration((value) => value + 1)
        }}
      />
    )

  return { field, prepare, spent }
}
