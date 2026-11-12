import * as React from "react"
import { ShieldAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { Field, FieldError } from "@authsystem/ui/field"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"
import { cn } from "@authsystem/ui/utils"

import type { BotAction, CheckState, TokenSlot } from "./token-slot"
import { isRetryableTurnstileError, turnstileLanguage } from "./turnstile-errors"
import { loadTurnstile, type TurnstileApi } from "./turnstile-script"

/**
 * The bot check's field: Cloudflare's widget, rendered for `action`, and the
 * sentences it needs when the person has something to do. One per page
 * (`useBotCheck` keys it), inside the form's FieldGroup, before the submit.
 */
export function BotCheckField({
  siteKey,
  action,
  generation,
  slot,
  state,
  onState,
  configFailed,
  onRetry,
}: {
  siteKey: string | null
  action: BotAction
  generation: number
  slot: TokenSlot
  state: CheckState
  onState: (state: CheckState) => void
  configFailed: boolean
  onRetry: () => void
}) {
  const { t, i18n } = useTranslation()
  const theme = useResolvedTheme()
  const language = turnstileLanguage(i18n.language)
  const containerRef = React.useRef<HTMLDivElement>(null)

  React.useEffect(() => {
    if (siteKey === null) return
    let cancelled = false
    let turnstile: TurnstileApi | undefined
    let widgetId: string | undefined
    slot.begin(action, () => {
      if (turnstile && widgetId) turnstile.reset(widgetId)
    })

    loadTurnstile().then(
      (api) => {
        const container = containerRef.current
        if (cancelled || !container) return
        turnstile = api
        widgetId = api.render(container, {
          sitekey: siteKey,
          action,
          language,
          theme,
          appearance: "interaction-only",
          size: "flexible",
          retry: "auto",
          "refresh-expired": "auto",
          callback: (token) => {
            slot.put(token, action)
            onState({ kind: "quiet" })
          },
          "error-callback": (code) => {
            slot.clear()
            if (!isRetryableTurnstileError(String(code))) {
              // Cloudflare says trying again cannot help: the waiting submits stop,
              // and the field says what the person can do instead.
              slot.fail()
              onState({ kind: "blocked", code: String(code) })
            }
            // Handled here. For a retryable code the widget's own retry runs, and a
            // submit waiting for its token keeps waiting (ENFORCED_WAIT_MS bounds it).
            return true
          },
          "expired-callback": () => slot.clear(),
          "timeout-callback": () => slot.clear(),
          "before-interactive-callback": () => onState({ kind: "interactive" }),
          "after-interactive-callback": () => onState({ kind: "quiet" }),
          "unsupported-callback": () => {
            slot.fail()
            onState({ kind: "blocked", code: "unsupported" })
          },
        })
      },
      () => {
        if (cancelled) return
        slot.fail()
        onState({ kind: "load-failed" })
      }
    )

    return () => {
      cancelled = true
      // A submit waiting for this action's token keeps waiting: the next widget makes it.
      slot.end()
      if (turnstile && widgetId) turnstile.remove(widgetId)
    }
    // A new widget for a new key, action, language or theme, or after "Try again".
  }, [siteKey, action, language, theme, generation, slot, onState])

  // Leaving the page: a submit still waiting gets its answer at once.
  React.useEffect(() => () => slot.fail(), [slot])

  const problem =
    configFailed && siteKey === null
      ? { title: t("botCheck.configFailedTitle"), body: t("botCheck.configFailedBody") }
      : state.kind === "blocked"
        ? { title: t("botCheck.blockedTitle"), body: t("botCheck.blockedBody") }
        : state.kind === "load-failed"
          ? { title: t("botCheck.loadFailedTitle"), body: t("botCheck.loadFailedBody") }
          : null
  // Nothing to show while the check runs unseen: the field then takes no place in
  // the form (out of the flow, so the group's gap is not spent on it), but the
  // widget stays in the page, where Cloudflare can still run its check.
  const visible = problem !== null || state.kind === "interactive" || state.kind === "incomplete"

  return (
    <Field
      // Its own control is Cloudflare's frame, so the group is named, not labelled.
      aria-label={t("botCheck.label")}
      data-slot="bot-check"
      data-testid="bot-check"
      data-state={state.kind}
      data-invalid={state.kind === "incomplete" || undefined}
      className={cn(!visible && "sr-only")}
    >
      <div ref={containerRef} data-testid="bot-check-widget" />
      {state.kind === "incomplete" ? <FieldError>{t("botCheck.completeTheCheck")}</FieldError> : null}
      {problem ? (
        <Alert variant="destructive" data-testid="bot-check-problem">
          <ShieldAlert />
          <AlertTitle>{problem.title}</AlertTitle>
          <AlertDescription>
            {problem.body}
            <Button type="button" variant="outline" size="sm" onClick={onRetry}>
              {t("common.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
    </Field>
  )
}
