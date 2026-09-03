import type { Control, FieldPath, FieldValues } from "react-hook-form"

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
import { Input } from "@authsystem/ui/input"
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
  const { policy } = usePasswordPolicy()

  return (
    <FormField
      control={control}
      name={name}
      render={({ field, fieldState }) => {
        const value = typeof field.value === "string" ? field.value : ""
        return (
          <FormItem data-invalid={fieldState.invalid} className={className}>
            <FormLabel>{label}</FormLabel>
            <FormControl>
              <Input
                type="password"
                autoComplete="new-password"
                autoFocus={autoFocus}
                {...field}
                value={value}
              />
            </FormControl>
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
