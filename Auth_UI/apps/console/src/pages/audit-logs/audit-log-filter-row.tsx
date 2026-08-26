import * as React from "react"
import { useTranslation } from "react-i18next"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { DateRangePicker } from "@authsystem/ui/common/date-range-picker"
import { SearchableSelect } from "@authsystem/ui/common/searchable-select"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"

import { AUDIT_ACTIONS, AUDIT_ACTION_TYPES } from "@/lib/audit-catalog"

import { useAuditLabels } from "./audit-log-labels"
import type { AuditLogFilters } from "./audit-log-filters"

/** Sentinel for "do not narrow", since a Select item cannot carry an empty value. */
const ALL = "__all__"

/**
 * Every way an audit list can be narrowed, on every screen that offers one.
 *
 * All five, everywhere. Per-surface hiding was considered and rejected: hiding
 * a COLUMN is safe — the rows are unchanged and the field is one menu entry
 * away — but hiding a FILTER that carries a value changes the row set with
 * nothing on screen to say why, which is the same class of lie as a filter that
 * is accepted and dropped. Offering a smaller set on the narrower surface would
 * only be honest alongside a chip row for whatever it hid, and five controls
 * that always show what they are doing is the simpler honest answer.
 */
export function AuditLogFilterRow({
  filters,
  onChange,
}: {
  filters: AuditLogFilters
  /** Partial, so a control that owns one axis cannot clear the others. */
  onChange: (next: Partial<AuditLogFilters>) => void
}) {
  const { t } = useTranslation()
  const { actionTypeLabel, actionLabel } = useAuditLabels()

  const actionOptions = React.useMemo(() => {
    const options = [
      { id: ALL, label: t("auditLogs.allActions") },
      ...AUDIT_ACTIONS.map((entry) => ({
        id: entry.code,
        label: actionLabel(entry.code),
        description: entry.code,
      })),
    ]
    // A saved link can carry a code this build has never heard of — a row from
    // before an action was retired, or a server one release ahead. Without an
    // option for it the trigger would show the placeholder while the table below
    // stays narrowed, and the reader would take a filtered page for the whole
    // table.
    if (
      filters.action &&
      !AUDIT_ACTIONS.some((entry) => entry.code === filters.action)
    ) {
      options.splice(1, 0, {
        id: filters.action,
        label: filters.action,
        description: filters.action,
      })
    }
    return options
  }, [filters.action, actionLabel, t])

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
      <ApplicationSelect
        value={filters.applicationId || undefined}
        onChange={(value) => onChange({ applicationId: value ?? "" })}
        allowAll
        className="w-full"
      />
      <Select
        value={filters.actionType || ALL}
        onValueChange={(value) =>
          onChange({ actionType: value === ALL ? "" : value })
        }
      >
        <SelectTrigger className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL}>{t("auditLogs.allActionTypes")}</SelectItem>
          {AUDIT_ACTION_TYPES.map((type) => (
            <SelectItem key={type} value={type}>
              {actionTypeLabel(type)}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {/* Searchable rather than a plain list: forty-nine actions is past what a
          dropdown can be scanned for, and the search matches the translated name
          AND the raw code, so both ways of knowing an action work. */}
      <SearchableSelect
        value={filters.action || ALL}
        options={actionOptions}
        onChange={(id) => onChange({ action: !id || id === ALL ? "" : id })}
        placeholder={t("auditLogs.searchAction")}
      />
      {/* Two choices, not three: the API matches the outcome on equality, so
          rows whose outcome was never recorded cannot be asked for. Offering
          "not recorded" here would be a filter that always returns nothing. */}
      <Select
        value={filters.result || ALL}
        onValueChange={(value) =>
          onChange({ result: value === ALL ? "" : value })
        }
      >
        <SelectTrigger className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL}>{t("auditLogs.allResults")}</SelectItem>
          <SelectItem value="true">{t("auditLogs.success")}</SelectItem>
          <SelectItem value="false">{t("auditLogs.failure")}</SelectItem>
        </SelectContent>
      </Select>
      <DateRangePicker
        from={filters.from || undefined}
        to={filters.to || undefined}
        onChange={({ from, to }) => onChange({ from: from ?? "", to: to ?? "" })}
      />
    </div>
  )
}
