import { useTranslation } from "react-i18next"

import { LoginDesignsManager } from "@/components/login-designs/login-designs-manager"

const PLATFORM_SCOPE = { kind: "platform" } as const

/**
 * The platform's sign-in page designs: the panel of the platform's own sign-in pages, and
 * of every application's that has no active design of its own. The platform settings
 * "Templates" tab, in its own chunk, so the page does not carry the list until it opens.
 */
export default function LoginDesignsTab() {
  const { t } = useTranslation()

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("loginDesigns.cardDescription")}</p>
      {/* The whole page needs platform-settings:manage, so whoever sees the tab may edit. */}
      <LoginDesignsManager
        scope={PLATFORM_SCOPE}
        canManage
        tableId="platform-login-designs"
        urlNamespace="designs"
      />
    </div>
  )
}
