import { useTranslation } from "react-i18next"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { Progress } from "@astoom/ui/progress"
import { Separator } from "@astoom/ui/separator"
import { Skeleton } from "@astoom/ui/skeleton"
import { numberLocale } from "@astoom/ui/format"

import { ORDINAL } from "./chart-constants"

/** One dormancy band; bands are cumulative and therefore ordered. */
type Band = { label: string; value: number }

/**
 * Account hygiene: MFA adoption as a meter, plus the dormancy bands.
 *
 * MFA adoption is a single ratio against a limit, so it is a meter — not a
 * two-slice donut, which is the least readable way to show one percentage. The
 * dormancy bands are cumulative (30 ⊇ 60 ⊇ 90 days), so they are genuinely ordinal
 * and take the ordered ramp.
 */
export function HygieneCard({
  mfaEnabled,
  activeUsers,
  dormant30,
  dormant60,
  dormant90,
  neverLoggedIn,
  loading,
}: {
  mfaEnabled: number
  activeUsers: number
  dormant30: number
  dormant60: number
  dormant90: number
  neverLoggedIn: number
  loading: boolean
}) {
  const { t } = useTranslation()
  const locale = numberLocale()

  const adoption =
    activeUsers > 0 ? Math.round((mfaEnabled / activeUsers) * 100) : null

  const bands: Band[] = [
    { label: t("dashboard.dormant30"), value: dormant30 },
    { label: t("dashboard.dormant60"), value: dormant60 },
    { label: t("dashboard.dormant90"), value: dormant90 },
  ]
  const widest = Math.max(...bands.map((band) => band.value), 1)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("dashboard.accountHygiene")}</CardTitle>
        <CardDescription>{t("dashboard.accountHygieneSubtitle")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="flex flex-col gap-2">
          <div className="flex items-baseline justify-between gap-3">
            <span className="text-sm text-muted-foreground">
              {t("dashboard.mfaAdoption")}
            </span>
            {loading ? (
              <Skeleton className="h-5 w-12" />
            ) : (
              <span className="text-lg font-semibold">
                {adoption !== null ? `${adoption}%` : "—"}
              </span>
            )}
          </div>
          {loading ? (
            <Skeleton className="h-3 w-full rounded-full" />
          ) : (
            <>
              <Progress
                value={adoption ?? 0}
                aria-label={t("dashboard.mfaAdoption")}
              />
              <p className="text-xs text-muted-foreground">
                {t("dashboard.mfaAdoptionHint", {
                  enabled: mfaEnabled,
                  active: activeUsers,
                })}
              </p>
            </>
          )}
        </div>

        <Separator />

        <div className="flex flex-col gap-3">
          <p className="text-sm text-muted-foreground">
            {t("dashboard.dormantAccounts")}
          </p>
          {loading ? (
            <Skeleton className="h-16 w-full" />
          ) : (
            <ul className="flex flex-col gap-2">
              {bands.map((band, index) => (
                <li key={band.label} className="flex items-center gap-3 text-sm">
                  <span className="w-28 shrink-0 text-muted-foreground">
                    {band.label}
                  </span>
                  <span
                    aria-hidden
                    className="h-2 rounded-full"
                    style={{
                      width: `${Math.max(2, (band.value / widest) * 100)}%`,
                      maxWidth: "60%",
                      background: ORDINAL[index],
                    }}
                  />
                  <span className="tabular-nums">
                    {band.value.toLocaleString(locale)}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {loading ? null : (
            <p className="text-xs text-muted-foreground">
              {t("dashboard.neverLoggedInHint", { count: neverLoggedIn })}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
