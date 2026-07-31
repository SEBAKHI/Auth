import {
  AlertTriangle,
  Clock,
  KeySquare,
  Lock,
  ShieldAlert,
  TrendingDown,
} from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import type { Schemas } from "@authsystem/api/types"
import { toNumber } from "@authsystem/api/helpers"

import { daysUntil, successRate } from "./helpers"

/** A drop of more than this many points in success rate is worth interrupting for. */
const RATE_DROP_POINTS = 5
/** Failures from one address at or above this count look like an attack, not typos. */
const IP_FAILURE_FLOOR = 10
/** An enablement expiring inside this many days needs a renewal decision now. */
const EXPIRY_HORIZON_DAYS = 30

type Finding = {
  key: string
  icon: LucideIcon
  title: string
  description: string
  to: string
  actionLabel: string
  destructive?: boolean
}

/**
 * Things that need a human, surfaced only when a threshold actually trips.
 *
 * Every input here was already on the wire and rendered nowhere: the old dashboard
 * showed a lockout count as one tile among ten and left the reader to notice it.
 * An empty panel is the normal state and renders nothing at all, which is what
 * makes a non-empty one meaningful.
 */
export function AttentionPanel({
  authStats,
  sessionStats,
  appActivity,
  loading,
}: {
  authStats?: Schemas["AuthStatsDto"]
  sessionStats?: Schemas["SessionStatsDto"]
  appActivity?: Schemas["AppActivityDto"]
  loading: boolean
}) {
  const { t } = useTranslation()

  if (loading) return null

  const findings: Finding[] = []

  const rate = successRate(
    toNumber(authStats?.windowSuccessCount),
    toNumber(authStats?.windowFailureCount)
  )
  const previousRate = successRate(
    toNumber(authStats?.previousWindowSuccessCount),
    toNumber(authStats?.previousWindowFailureCount)
  )
  if (rate !== null && previousRate !== null) {
    const drop = Math.round((previousRate - rate) * 10) / 10
    if (drop > RATE_DROP_POINTS) {
      findings.push({
        key: "rate-drop",
        icon: TrendingDown,
        destructive: true,
        title: t("dashboard.alertRateDrop"),
        description: t("dashboard.alertRateDropBody", {
          points: drop,
          rate,
        }),
        to: "/audit-logs",
        actionLabel: t("dashboard.viewAuditLogs"),
      })
    }
  }

  const lockedOut = toNumber(authStats?.lockedOutNow)
  if (lockedOut > 0) {
    findings.push({
      key: "locked-out",
      icon: Lock,
      destructive: true,
      title: t("dashboard.alertLockedOut", { count: lockedOut }),
      description: t("dashboard.alertLockedOutBody"),
      to: "/users",
      actionLabel: t("dashboard.reviewUsers"),
    })
  }

  const worstIp = (authStats?.topFailingIps ?? []).find(
    (row) => toNumber(row.failureCount) >= IP_FAILURE_FLOOR
  )
  if (worstIp) {
    findings.push({
      key: "failing-ip",
      icon: ShieldAlert,
      destructive: true,
      title: t("dashboard.alertFailingIp"),
      description: t("dashboard.alertFailingIpBody", {
        ip: worstIp.ipAddress ?? t("common.unknown"),
        count: toNumber(worstIp.failureCount),
        users: toNumber(worstIp.distinctUsernames),
      }),
      to: "/audit-logs",
      actionLabel: t("dashboard.viewAuditLogs"),
    })
  }

  const stale = toNumber(sessionStats?.staleOpenSessions)
  if (stale > 0) {
    findings.push({
      key: "stale-sessions",
      icon: Clock,
      title: t("dashboard.alertStaleSessions", { count: stale }),
      description: t("dashboard.alertStaleSessionsBody"),
      to: "/audit-logs",
      actionLabel: t("dashboard.viewAuditLogs"),
    })
  }

  const expiringTokens = toNumber(sessionStats?.tokensExpiringIn7Days)
  if (expiringTokens > 0) {
    findings.push({
      key: "expiring-tokens",
      icon: KeySquare,
      title: t("dashboard.alertExpiringTokens", { count: expiringTokens }),
      description: t("dashboard.alertExpiringTokensBody"),
      to: "/api-keys",
      actionLabel: t("dashboard.reviewKeys"),
    })
  }

  const expiringEnablements = (appActivity?.organizationApplications ?? []).filter(
    (row) => {
      const remaining = daysUntil(row.expiresAt)
      return remaining !== null && remaining <= EXPIRY_HORIZON_DAYS
    }
  )
  if (expiringEnablements.length > 0) {
    findings.push({
      key: "expiring-enablements",
      icon: AlertTriangle,
      title: t("dashboard.alertExpiringEnablements", {
        count: expiringEnablements.length,
      }),
      description: t("dashboard.alertExpiringEnablementsBody", {
        days: EXPIRY_HORIZON_DAYS,
      }),
      to: "/organizations",
      actionLabel: t("dashboard.reviewOrganizations"),
    })
  }

  if (findings.length === 0) return null

  return (
    <section className="flex flex-col gap-3" aria-label={t("dashboard.attention")}>
      {findings.map((finding) => (
        <Alert
          key={finding.key}
          variant={finding.destructive ? "destructive" : "default"}
        >
          <finding.icon />
          <AlertTitle>{finding.title}</AlertTitle>
          <AlertDescription className="flex flex-wrap items-center justify-between gap-2">
            <span>{finding.description}</span>
            <Button asChild variant="outline" size="sm">
              <Link to={finding.to}>{finding.actionLabel}</Link>
            </Button>
          </AlertDescription>
        </Alert>
      ))}
    </section>
  )
}
