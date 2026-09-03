import type { FieldValues, Path, UseFormReturn } from "react-hook-form"
import { z } from "zod"

import i18n from "@authsystem/i18n"
import { getErrorDescriptions } from "@authsystem/api/errors"
import {
  evaluatePassword,
  FALLBACK_PASSWORD_POLICY,
  type PasswordPolicy,
} from "@authsystem/api/password-policy"

/**
 * The one local judgement on a new password, shared by every form that takes
 * one: the message to show, or undefined when the value satisfies every rule
 * the policy enables. While the policy is unknown only the registry floor is
 * enforced (see FALLBACK_PASSWORD_POLICY) — the server remains the authority
 * and its verdict reaches the field through {@link applyPasswordServerErrors}.
 *
 * One sentence rather than one per rule: the requirement list beside the
 * control already names each rule and its state, so the message only has to
 * say that the list is not yet satisfied. Read from `i18n.t` at validation
 * time, not a `t` captured at render, so a language switched mid-form is
 * honoured by the next validation.
 */
export function passwordIssue(
  value: string,
  policy: PasswordPolicy | undefined
): string | undefined {
  if (value.length === 0) return i18n.t("validation.required")
  const rules = evaluatePassword(value, policy ?? FALLBACK_PASSWORD_POLICY)
  return rules.some((rule) => !rule.met)
    ? i18n.t("auth.passwordDoesNotMeetRules")
    : undefined
}

/**
 * A zod string enforcing the live policy. Build it per render, as the forms
 * do: react-hook-form re-reads its resolver on every render, so the schema
 * tightens the moment the policy arrives without any effect or remount.
 */
export function passwordSchema(policy: PasswordPolicy | undefined) {
  return z.string().superRefine((value, ctx) => {
    const message = passwordIssue(value, policy)
    if (message) ctx.addIssue({ code: "custom", message })
  })
}

/**
 * Puts the server's verdict on the password where the person is looking, all
 * of it at once.
 *
 * `PasswordValidator` returns every rule a password broke, and the API ships
 * them all in the ProblemDetails `errors` array — but a toast of
 * `getErrorMessage` shows the first sentence only, which is how a person came
 * to discover the policy one rule per submit. `setError` with `types` carries
 * the whole list, and `FormMessage` renders it as one.
 *
 * Returns false when the failure was not about the password, so the caller
 * can fall back to whatever feedback it already had.
 */
export function applyPasswordServerErrors<TFieldValues extends FieldValues>(
  form: Pick<UseFormReturn<TFieldValues>, "setError" | "setFocus">,
  name: Path<TFieldValues>,
  error: unknown
): boolean {
  const sentences = getErrorDescriptions(error)
    .filter((entry) => entry.code.startsWith("Password."))
    .map((entry) => entry.description)
  if (sentences.length === 0) return false

  form.setError(name, {
    type: "server",
    message: sentences[0],
    types: Object.fromEntries(
      sentences.map((sentence, index) => [`server-${index}`, sentence])
    ),
  })
  form.setFocus(name)
  return true
}
