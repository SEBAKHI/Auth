"use client"

import * as React from "react"
import { Slot } from "radix-ui"
import {
  Controller,
  FormProvider,
  useFormContext,
  useFormState,
  type ControllerProps,
  type FieldPath,
  type FieldValues,
} from "react-hook-form"

import { cn } from "@authsystem/ui/utils"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@authsystem/ui/field"

const Form = FormProvider

type FormFieldContextValue<
  TFieldValues extends FieldValues = FieldValues,
  TName extends FieldPath<TFieldValues> = FieldPath<TFieldValues>,
> = {
  name: TName
}

const FormFieldContext = React.createContext<FormFieldContextValue>(
  {} as FormFieldContextValue
)

const FormField = <
  TFieldValues extends FieldValues = FieldValues,
  TName extends FieldPath<TFieldValues> = FieldPath<TFieldValues>,
>({
  ...props
}: ControllerProps<TFieldValues, TName>) => {
  return (
    <FormFieldContext.Provider value={{ name: props.name }}>
      <Controller {...props} />
    </FormFieldContext.Provider>
  )
}

function useFormField() {
  const fieldContext = React.useContext(FormFieldContext)
  const itemContext = React.useContext(FormItemContext)
  const { getFieldState } = useFormContext()
  const formState = useFormState({ name: fieldContext.name })
  const fieldState = getFieldState(fieldContext.name, formState)

  if (!fieldContext) {
    throw new Error("useFormField should be used within <FormField>")
  }

  const { id } = itemContext

  return {
    id,
    name: fieldContext.name,
    formItemId: `${id}-form-item`,
    formDescriptionId: `${id}-form-item-description`,
    formMessageId: `${id}-form-item-message`,
    ...fieldState,
  }
}

type FormItemContextValue = {
  id: string
}

const FormItemContext = React.createContext<FormItemContextValue>(
  {} as FormItemContextValue
)

function FormItem({ className, ...props }: React.ComponentProps<typeof Field>) {
  const id = React.useId()

  // Field owns the in-field spacing (label↔control↔error) per the Luma preset;
  // FieldGroup owns the spacing between fields. No utility spacing here.
  return (
    <FormItemContext.Provider value={{ id }}>
      <Field className={className} {...props} />
    </FormItemContext.Provider>
  )
}

function FormLabel({
  className,
  ...props
}: React.ComponentProps<typeof FieldLabel>) {
  const { error, formItemId } = useFormField()

  return (
    <FieldLabel
      data-error={!!error}
      className={cn("data-[error=true]:text-destructive", className)}
      htmlFor={formItemId}
      {...props}
    />
  )
}

function FormControl({ ...props }: React.ComponentProps<typeof Slot.Root>) {
  const { error, formItemId, formDescriptionId, formMessageId } = useFormField()

  return (
    <Slot.Root
      data-slot="form-control"
      id={formItemId}
      aria-describedby={
        !error
          ? `${formDescriptionId}`
          : `${formDescriptionId} ${formMessageId}`
      }
      aria-invalid={!!error}
      {...props}
    />
  )
}

function FormDescription({ className, ...props }: React.ComponentProps<"p">) {
  const { formDescriptionId } = useFormField()

  return (
    <FieldDescription id={formDescriptionId} className={className} {...props} />
  )
}

function FormMessage({
  className,
  children,
  ...props
}: React.ComponentProps<"div">) {
  const { error, formMessageId } = useFormField()

  // `setError(name, { types })` is how a caller hands one field SEVERAL
  // sentences — every password rule a submission broke, say. A lone `message`
  // would show the first and hide the rest, and hiding the rest is exactly the
  // one-rule-per-submit experience the callers that use `types` exist to end.
  const messages = error?.types
    ? Object.values(error.types).filter(
        (value): value is string =>
          typeof value === "string" && value.length > 0
      )
    : []
  if (messages.length > 1) {
    return (
      <FieldError
        id={formMessageId}
        className={className}
        errors={messages.map((message) => ({ message }))}
        {...props}
      />
    )
  }

  const body = error ? String(error?.message ?? "") : children

  if (!body) {
    return null
  }

  return (
    <FieldError id={formMessageId} className={className} {...props}>
      {body}
    </FieldError>
  )
}

export {
  useFormField,
  Form,
  FormItem,
  FormLabel,
  FormControl,
  FormDescription,
  FormMessage,
  FormField,
}
