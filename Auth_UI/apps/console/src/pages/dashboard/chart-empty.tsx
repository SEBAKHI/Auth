import { useTranslation } from "react-i18next"

/** Honest empty state shown instead of a chart when the window has no data. */
export function ChartEmpty() {
  const { t } = useTranslation()
  return (
    <p className="py-12 text-center text-sm text-muted-foreground">
      {t("dashboard.noData")}
    </p>
  )
}
