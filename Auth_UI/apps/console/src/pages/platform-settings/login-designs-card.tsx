import { useTranslation } from "react-i18next"

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@authsystem/ui/card"

import { LoginDesignsManager } from "@/components/login-designs/login-designs-manager"

const PLATFORM_SCOPE = { kind: "platform" } as const

/**
 * The platform's sign-in page designs: the panel of the platform's own sign-in pages, and
 * of every application's that has no active design of its own. Its own chunk, loaded
 * with the card, so the platform settings page does not carry the editor.
 */
export default function LoginDesignsCard() {
  const { t } = useTranslation()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("loginDesigns.cardTitle")}</CardTitle>
        <CardDescription>{t("loginDesigns.cardDescription")}</CardDescription>
      </CardHeader>
      <CardContent>
        {/* The whole page needs platform-settings:manage, so whoever sees the card may edit. */}
        <LoginDesignsManager scope={PLATFORM_SCOPE} canManage appearance={null} />
      </CardContent>
    </Card>
  )
}
