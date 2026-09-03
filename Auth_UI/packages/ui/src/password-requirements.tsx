import { Check, Circle } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import type {
  PasswordRuleId,
  PasswordRuleState,
} from "@authsystem/api/password-policy"
import { FieldDescription } from "@authsystem/ui/field"
import { cn } from "@authsystem/ui/utils"

const RULE_LABEL_KEYS: Record<PasswordRuleId, string> = {
  minLength: "auth.passwordRuleMinLength",
  uppercase: "auth.passwordRuleUppercase",
  lowercase: "auth.passwordRuleLowercase",
  digit: "auth.passwordRuleDigit",
  special: "auth.passwordRuleSpecial",
}

/**
 * The live checklist under a new-password control: one line per rule the
 * server's policy enables, each ticked or not from the value as it is typed.
 *
 * State is never carried by colour alone. The icon changes, and every line
 * ends with a visually hidden "met" / "not met yet", so a screen reader gets
 * what the eye gets. Progress is announced through ONE polite live region —
 * a per-line announcement on every keystroke is speech, not help.
 *
 * It also says, in plain words, that the server checks more than this list:
 * common patterns are judged only on submit, and a list that was all green a
 * moment before a refusal costs more trust than no list at all.
 *
 * Sits where a field description sits — after the control, before the error —
 * and takes its spacing from the enclosing Field, like every other line there.
 */
export function PasswordRequirements({
  rules,
  className,
  ...props
}: React.ComponentProps<"div"> & { rules: readonly PasswordRuleState[] }) {
  const { t } = useTranslation()
  const met = rules.filter((rule) => rule.met).length

  return (
    <div
      data-slot="password-requirements"
      className={cn("flex flex-col gap-1.5", className)}
      {...props}
    >
      <p className="sr-only" aria-live="polite">
        {t("auth.passwordRulesProgress", { met, total: rules.length })}
      </p>
      <ul
        role="list"
        aria-label={t("auth.passwordRulesTitle")}
        className="flex flex-col gap-1 text-xs leading-normal [&_svg]:size-3.5 [&_svg]:shrink-0"
      >
        {rules.map((rule) => (
          <li
            key={rule.id}
            data-slot="password-requirement"
            data-rule={rule.id}
            data-met={rule.met}
            className={cn(
              "flex items-center gap-2 text-start",
              rule.met ? "text-foreground" : "text-muted-foreground"
            )}
          >
            {rule.met ? (
              <Check aria-hidden="true" />
            ) : (
              <Circle aria-hidden="true" />
            )}
            <span>{t(RULE_LABEL_KEYS[rule.id], { count: rule.count })}</span>
            <span className="sr-only">
              {rule.met
                ? t("auth.passwordRuleMet")
                : t("auth.passwordRuleUnmet")}
            </span>
          </li>
        ))}
      </ul>
      <FieldDescription>{t("auth.passwordRulesAlsoChecked")}</FieldDescription>
    </div>
  )
}
