import { TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import { formatDate } from "@authsystem/ui/format"

import {
  ACTOR_RECORDED_FROM,
  AUDIT_PARTICIPANT_ROLES,
  type AuditParticipantRole,
} from "./audit-log-participant"

/**
 * Which of the two people on an audit row a person-scoped table is asking about.
 *
 * The choice exists because the row names two, and a table scoped to one person
 * had only ever been able to ask about the one it happened TO — so "what did
 * this operator do" was a question the trail could not be asked, on the screen
 * whose whole purpose is asking it.
 */
export function AuditParticipantFilter({
  value,
  onChange,
}: {
  value: AuditParticipantRole
  onChange: (role: AuditParticipantRole) => void
}) {
  const { t } = useTranslation()

  return (
    <ToggleGroup
      type="single"
      spacing={0}
      variant="outline"
      value={value}
      aria-label={t("auditLogs.participantRole")}
      onValueChange={(next) => {
        // An empty value is the group deselecting itself on a second click.
        // There is no "no role" here — the table always asks one of the three.
        if (!next) return
        onChange(next as AuditParticipantRole)
      }}
    >
      {AUDIT_PARTICIPANT_ROLES.map((role) => (
        <ToggleGroupItem key={role} value={role}>
          {t(`auditLogs.participantRoles.${role}`)}
        </ToggleGroupItem>
      ))}
    </ToggleGroup>
  )
}

/**
 * What the actor column cannot tell you about old rows.
 *
 * Shown for any role that reads the performer, because for rows written before
 * `ACTOR_RECORDED_FROM` the performer is a copy of the subject. Those rows do
 * not merely lack the answer — they carry a wrong one that looks like every
 * other row, and the filter that surfaces them is what makes it believable.
 */
export function ActorBoundaryNotice({ role }: { role: AuditParticipantRole }) {
  const { t } = useTranslation()

  if (role === "subject") return null

  return (
    <Alert>
      <TriangleAlert />
      <AlertDescription>
        {t("auditLogs.actorBoundary", {
          date: formatDate(ACTOR_RECORDED_FROM),
        })}
      </AlertDescription>
    </Alert>
  )
}
