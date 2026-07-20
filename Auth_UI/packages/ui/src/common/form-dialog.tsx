import { Loader2 } from "lucide-react"
import type * as React from "react"
import type { FieldValues, UseFormReturn } from "react-hook-form"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { FieldGroup } from "@astoom/ui/field"
import { Form } from "@astoom/ui/form"
import { useDirtyClose } from "@astoom/ui/hooks/use-dirty-close"

/**
 * Standard shell for a react-hook-form dialog. The unsaved-changes guard is
 * wired in by default from the form's dirty state, so every dialog built with
 * `FormDialog` warns before discarding edits without any per-form wiring.
 *
 * Render the form fields as children; the header, footer (Cancel + submit) and
 * the discard confirmation are provided. Programmatic closes after a successful
 * submit should call the original `onOpenChange(false)` to bypass the guard.
 */
export function FormDialog<T extends FieldValues>({
  open,
  onOpenChange,
  form,
  title,
  description,
  formId,
  onSubmit,
  submitLabel,
  pending = false,
  size,
  contentClassName,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  form: UseFormReturn<T>
  title: React.ReactNode
  description?: React.ReactNode
  formId: string
  onSubmit: (values: T) => void
  submitLabel: string
  pending?: boolean
  /** Width from the shared dialog scale; defaults to the dialog default (`lg`). */
  size?: React.ComponentProps<typeof DialogContent>["size"]
  contentClassName?: string
  children: React.ReactNode
}) {
  const { t } = useTranslation()
  const { requestOpenChange, discardDialog } = useDirtyClose({
    isDirty: form.formState.isDirty,
    onOpenChange,
  })

  return (
    <Dialog open={open} onOpenChange={requestOpenChange}>
      <DialogContent size={size} className={contentClassName}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description ? (
            <DialogDescription>{description}</DialogDescription>
          ) : null}
        </DialogHeader>

        <Form {...form}>
          <form id={formId} onSubmit={form.handleSubmit(onSubmit)}>
            <FieldGroup>{children}</FieldGroup>
          </form>
        </Form>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => requestOpenChange(false)}
            disabled={pending}
          >
            {t("common.cancel")}
          </Button>
          <Button type="submit" form={formId} disabled={pending}>
            {pending ? <Loader2 className="animate-spin" /> : null}
            {submitLabel}
          </Button>
        </DialogFooter>
        {discardDialog}
      </DialogContent>
    </Dialog>
  )
}
