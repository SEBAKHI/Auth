import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { DatePicker, monthsFromNow } from "@astoom/ui/common/date-picker"
import { PresetField } from "@astoom/ui/common/preset-field"
import { FieldGroup } from "@astoom/ui/field"
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { ENVIRONMENTS } from "@/lib/presets"
import { Button } from "@astoom/ui/button"
import { Input } from "@astoom/ui/input"
import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { Spinner } from "@astoom/ui/spinner"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

export function WebhookKeyCreateDialog({
  open,
  onOpenChange,
  applicationId,
  onCreated,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  applicationId: string
  onCreated: (webhookKey: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const schema = z.object({
    name: z.string().min(1, t("validation.required")),
    targetUrl: z.string().min(1, t("validation.required")),
    environment: z.string().optional(),
    description: z.string().optional(),
    expiresAt: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      targetUrl: "",
      environment: "production",
      description: "",
      expiresAt: "",
    },
  })

  React.useEffect(() => {
    if (open) form.reset()
  }, [open, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const { data, error } = await api.POST("/api/v1/WebhookKeys", {
        body: {
          applicationId,
          name: values.name,
          targetUrl: values.targetUrl,
          description: emptyToNull(values.description),
          environment: emptyToNull(values.environment),
          expiresAt: values.expiresAt
            ? new Date(values.expiresAt).toISOString()
            : null,
        },
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      void queryClient.invalidateQueries({ queryKey: ["webhook-keys"] })
      toast.success(t("webhookKeys.created"))
      onOpenChange(false)
      if (data?.webhookKey) onCreated(data.webhookKey)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("webhookKeys.createTitle")}</DialogTitle>
          <DialogDescription>{t("webhookKeys.subtitle")}</DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form
            id="webhook-key-form"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <FieldGroup>
              <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("common.name")}</FormLabel>
                    <FormControl>
                      <Input {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="targetUrl"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("webhookKeys.targetUrl")}</FormLabel>
                    <FormControl>
                      <Input placeholder="https://" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="environment"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("webhookKeys.environment")}</FormLabel>
                    <FormControl>
                      <PresetField
                        presets={ENVIRONMENTS}
                        value={field.value ?? ""}
                        onChange={field.onChange}
                      >
                        {({ value, onChange }) => (
                          <Input
                            value={value}
                            onChange={(event) => onChange(event.target.value)}
                          />
                        )}
                      </PresetField>
                    </FormControl>
                    <FormDescription>
                      {t("webhookKeys.environmentHint")}
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="description"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("common.description")}</FormLabel>
                    <FormControl>
                      <Input {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="expiresAt"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("common.expiresAt")}</FormLabel>
                    <FormControl>
                      <DatePicker
                        value={field.value}
                        onChange={(value) => field.onChange(value ?? "")}
                        minDate={new Date()}
                        maxDate={monthsFromNow(10)}
                        placeholder={t("common.never")}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </FieldGroup>
          </form>
        </Form>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={mutation.isPending}
          >
            {t("common.cancel")}
          </Button>
          <Button
            type="submit"
            form="webhook-key-form"
            disabled={mutation.isPending}
          >
            {mutation.isPending ? <Spinner /> : null}
            {t("common.create")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
