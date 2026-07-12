import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { Button } from "@astoom/ui/button"

/** Centered full-page status screen used for 403 / 404. */
export function StatusScreen({
  code,
  title,
  description,
}: {
  code: string
  title: string
  description: string
}) {
  const { t } = useTranslation()

  return (
    <div className="flex min-h-svh flex-col items-center justify-center gap-4 p-6 text-center">
      <p className="text-5xl font-bold text-muted-foreground">{code}</p>
      <div className="space-y-1">
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        <p className="max-w-sm text-sm text-muted-foreground">{description}</p>
      </div>
      <Button asChild>
        <Link to="/">{t("errors.goHome")}</Link>
      </Button>
    </div>
  )
}
