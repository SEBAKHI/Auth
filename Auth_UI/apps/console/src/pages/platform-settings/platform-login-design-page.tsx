import { useTranslation } from "react-i18next"

import { LoginDesignPage } from "@/components/login-designs/login-design-page"

const PLATFORM_SCOPE = { kind: "platform" } as const

/**
 * One of the platform's sign-in page designs on its own page. The route needs
 * platform-settings:manage, so whoever reaches it may edit; the preview follows the
 * console's own appearance.
 */
export function PlatformLoginDesignPage() {
  const { t } = useTranslation()
  return (
    <LoginDesignPage
      scope={PLATFORM_SCOPE}
      canManage
      appearance={null}
      description={t("loginDesigns.cardDescription")}
    />
  )
}
