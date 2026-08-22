import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { POLICY_TOKENS } from "@authsystem/ui/common/policy-document"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@authsystem/ui/tooltip"

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
