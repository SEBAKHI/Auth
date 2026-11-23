import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { Field, FieldContent, FieldDescription, FieldLabel } from "@authsystem/ui/field"
import { Switch } from "@authsystem/ui/switch"

import { LoginDesignsManager } from "@/components/login-designs/login-designs-manager"

import { PLATFORM_SETTINGS_QUERY_KEY } from "./platform-settings-tabs"

const PLATFORM_SCOPE = { kind: "platform" } as const

/**
 * The platform's sign-in page designs: the panel of the platform's own sign-in pages, and of
 * every application's that has no active design of its own. The platform settings
 * "Templates" tab, in its own chunk, so the page does not carry the list until it opens.
 *
 * Above the list, the switch that shows or hides the panel itself: the designs fill the
 * panel, so whether there is one at all is decided where they are. Saved on the click, as the
 * application's "Available" switch is: there is nothing else to fill in.
 */
export default function LoginDesignsTab({ settings }: { settings: Schemas["PlatformSettingsDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const shown = settings.loginPanelShown !== false

  const save = useMutation({
    mutationFn: async (next: boolean) => {
      const { data, error } = await api.PUT("/api/v1/admin/platform-settings/login-panel", {
        body: { shown: next },
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      // The switch alone into the page's copy, as the logo height is: a whole answer landing
      // out of turn could hand an older logo back to the branding card's next save.
      queryClient.setQueryData<Schemas["PlatformSettingsDto"]>(PLATFORM_SETTINGS_QUERY_KEY, (old) =>
        old ? { ...old, loginPanelShown: data?.loginPanelShown ?? true } : old
      )
      void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
      toast.success(t("platformSettings.updated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("loginDesigns.cardDescription")}</p>
      <Field orientation="horizontal">
        <FieldContent>
          <FieldLabel htmlFor="login-panel-shown">{t("loginDesigns.panelShown")}</FieldLabel>
          <FieldDescription>{t("loginDesigns.panelShownHint")}</FieldDescription>
        </FieldContent>
        {/* The whole page needs platform-settings:manage, so whoever sees the tab may switch it. */}
        <Switch
          id="login-panel-shown"
          checked={save.isPending ? save.variables : shown}
          disabled={save.isPending}
          onCheckedChange={(next) => save.mutate(next)}
        />
      </Field>
      <LoginDesignsManager
        scope={PLATFORM_SCOPE}
        canManage
        tableId="platform-login-designs"
        urlNamespace="designs"
      />
    </div>
  )
}
