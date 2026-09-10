import { Eye, EyeOff } from "lucide-react"
import * as React from "react"
import type { Control, FieldPath, FieldValues } from "react-hook-form"
import { useTranslation } from "react-i18next"

import {
  evaluatePassword,
  usePasswordPolicy,
} from "@authsystem/api/password-policy"
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from "@authsystem/ui/input-group"
import { PasswordRequirements } from "@authsystem/ui/password-requirements"

interface PasswordFieldProps<
  TFieldValues extends FieldValues,
  TName extends FieldPath<TFieldValues>,
> {
  control: Control<TFieldValues>
  name: TName
  label: string
  autoFocus?: boolean
  className?: string
}

/**
 * A new-password control with the live requirement list beneath it — the
 * whole field, so the six forms that take a new password compose one thing
 * and cannot drift from each other in what they show. The rules it enforces
 * on submit live beside it in `password-rules.ts`.
 *
 * The list appears only once the policy is known. Nothing is drawn from a
 * guess while it loads, and nothing at all when it cannot be fetched: the
 * schema then enforces the floor and the server's answer fills the message.
 * Order follows the Field contract — label, control, description, error — so
 * the list reads as the description of the control it sits under.
 *
 * A show/hide toggle replaces the confirm-password field the forms used to
 * carry. Retyping catches a typo only if the typo is not retyped, and a
 * person who can see what they wrote has no typo to catch; the toggle is the
 * cheaper check and the one that works with a password manager. The toggle
 * sits in the input group's addon, so the control keeps its id and the label
 * stays associated with the input rather than the button. It is a plain
 * action button whose name says what a press will do; no aria-pressed, which
 * beside a changing name announces the opposite of the visible state.
 */
export function PasswordField<
  TFieldValues extends FieldValues,
  TName extends FieldPath<TFieldValues>,
>({
  control,
  name,
  label,
  autoFocus,
  className,
}: PasswordFieldProps<TFieldValues, TName>) {
  const { t } = useTranslation()
  const { policy } = usePasswordPolicy()
  const [visible, setVisible] = React.useState(false)

  return (
    <FormField
      control={control}
      name={name}
      render={({ field, fieldState }) => {
        const value = typeof field.value === "string" ? field.value : ""
        return (
          <FormItem data-invalid={fieldState.invalid} className={className}>
            <FormLabel>{label}</FormLabel>
            <InputGroup>
              <FormControl>
                <InputGroupInput
                  // FormControl is a Slot and stamps its own data-slot on the
                  // child; the child's value wins the merge, and InputGroup's
                  // focus ring is keyed on this one. Without it the field has
                  // no visible keyboard focus.
                  data-slot="input-group-control"
                  type={visible ? "text" : "password"}
                  autoComplete="new-password"
                  autoFocus={autoFocus}
                  {...field}
                  value={value}
                />
              </FormControl>
              <InputGroupAddon align="inline-end">
                <InputGroupButton
                  size="icon-xs"
                  aria-label={
                    visible ? t("auth.hidePassword") : t("auth.showPassword")
                  }
                  onClick={() => setVisible((current) => !current)}
                >
                  {visible ? <EyeOff /> : <Eye />}
                </InputGroupButton>
              </InputGroupAddon>
            </InputGroup>
            {policy ? (
              <PasswordRequirements rules={evaluatePassword(value, policy)} />
            ) : null}
            <FormMessage />
          </FormItem>
        )
      }}
    />
  )
}
