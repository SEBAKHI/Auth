import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { Loader2 } from "lucide-react"
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
} from "@/components/ui/dialog"
import { FieldGroup } from "@/components/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { api } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"

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
                      <Input {...field} />
                    </FormControl>
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
                      <Input type="date" {...field} />
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
            {mutation.isPending ? <Loader2 className="animate-spin" /> : null}
            {t("common.create")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
