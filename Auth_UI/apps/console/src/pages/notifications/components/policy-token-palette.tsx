import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import { POLICY_TOKENS } from "@astoom/ui/common/policy-document"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@astoom/ui/tooltip"

type EditableField = HTMLInputElement | HTMLTextAreaElement

/**
 * Tracks the last focused text field so a palette click can insert at its
 * cursor. Focus has already moved to the palette button by click time, which
 * is why the target must be remembered on focus rather than read on click.
 */
export function useFocusedField() {
  const fieldRef = React.useRef<EditableField | null>(null)

  const onFocusCapture = React.useCallback((event: React.FocusEvent) => {
    const target = event.target as HTMLElement
    if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) {
      fieldRef.current = target
    }
  }, [])

  /**
   * Inserts text at the remembered cursor. React owns these inputs, so the
   * value is written through the native setter and an input event dispatched
   * — assigning `.value` directly would be swallowed by React's value tracker
   * and the state would never update.
   */
  const insert = React.useCallback((text: string) => {
    const field = fieldRef.current
    if (!field || field.disabled || field.readOnly) return false

    const start = field.selectionStart ?? field.value.length
    const end = field.selectionEnd ?? start
    const next = field.value.slice(0, start) + text + field.value.slice(end)

    const prototype =
      field instanceof HTMLTextAreaElement
        ? HTMLTextAreaElement.prototype
        : HTMLInputElement.prototype
    const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set
    setter?.call(field, next)
    field.dispatchEvent(new Event("input", { bubbles: true }))

    const caret = start + text.length
    field.focus()
    field.setSelectionRange(caret, caret)
    return true
  }, [])

  return { onFocusCapture, insert }
}

/**
 * Clickable palette of the configuration-driven placeholders. One click drops
 * the token at the cursor of the field you were last editing — numbers are
 * never meant to be typed by hand, since the published page substitutes these
 * from the running settings.
 */
export function PolicyTokenPalette({
  onInsert,
  disabled,
}: {
  onInsert: (token: string) => boolean
  disabled?: boolean
}) {
  const { t } = useTranslation()

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="text-xs text-muted-foreground">
        {t("notifications.policyTokens")}
      </span>
      {POLICY_TOKENS.map((token) => (
        <Tooltip key={token}>
          <TooltipTrigger asChild>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="font-mono"
              disabled={disabled}
              onClick={() => onInsert(token)}
            >
              {/* Liquid source, and its braces are mirrored characters: without an
                  isolate an RTL console drew `{{graceDays}}` as `}}graceDays{{`. */}
              <bdi dir="ltr">{token}</bdi>
            </Button>
          </TooltipTrigger>
          <TooltipContent>
            <p>{t(`notifications.policyToken_${token.replace(/[{}]/g, "")}`)}</p>
            <p className="text-muted-foreground">
              {t("notifications.policyTokenHint")}
            </p>
          </TooltipContent>
        </Tooltip>
      ))}
    </div>
  )
}
