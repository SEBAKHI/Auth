import { useTranslation } from "react-i18next"

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { useApplications } from "@authsystem/ui/hooks/use-applications"

const ALL_VALUE = "__all__"

/** Application picker backed by the cached applications list. */
export function ApplicationSelect({
  id,
  value,
  onChange,
  allowAll = false,
  allowPlatform = false,
  placeholder,
  className,
  options,
  loading,
}: {
  /** Trigger id, so a `FieldLabel htmlFor` can name the control. */
  id?: string
  value: string | undefined
  onChange: (value: string | undefined) => void
  allowAll?: boolean
  /**
   * Offers "no application" as a real choice meaning the platform itself, for
   * the screens where an absent application is a scope rather than a blank.
   *
   * Distinct from `allowAll`, which is a filter meaning "do not narrow". Both
   * resolve to `undefined`; they differ in what that says. Permissions are the
   * case: every code the API enforces on itself is stored with a null
   * ApplicationId, so without this the console could define a permission for a
   * registered application and not for the platform.
   */
  allowPlatform?: boolean
  placeholder?: string
  className?: string
  /**
   * Explicit list, replacing the platform-wide one.
   *
   * Callers that need a narrower set pass it here rather than filtering
   * downstream: this component is shared by nine screens whose needs differ —
   * an organization may only enable applications open to everyone, while the
   * role, permission and API-key screens must be able to reach every one.
   */
  options?: { id?: string; name?: string }[]
  /** Loading flag for a caller-supplied list. */
  loading?: boolean
}) {
  const { t } = useTranslation()
  const shared = useApplications()
  const source = options ?? shared.data?.applications ?? []
  const isLoading = options ? Boolean(loading) : shared.isLoading
  const apps = source.filter((app) => Boolean(app.id))
  // With no selectable options, keep the trigger disabled so it never opens an
  // empty popover (an empty Radix Select inside a Dialog can otherwise leak a
  // dismiss that closes the dialog).
  const isEmpty = apps.length === 0 && !allowAll && !allowPlatform
  // "You may not see this list" is a different fact from "this list is empty",
  // and only one of them is the reader's to fix. Conflating them left a holder
  // of apikeys:create staring at a disabled picker reading "No applications"
  // with no hint that applications:read was the missing piece.
  const isForbidden = !options && shared.isForbidden
  // Distinguish "still loading" from "genuinely empty" so the trigger does not
  // briefly read "No applications" before the list arrives.
  const placeholder_ = isLoading
    ? t("common.loading")
    : isForbidden
      ? t("common.applicationsUnavailable")
      : isEmpty
        ? t("common.noApplications")
        : (placeholder ?? t("common.selectApplication"))

  return (
    <Select
      value={value ?? (allowAll || allowPlatform ? ALL_VALUE : undefined)}
      onValueChange={(next) => onChange(next === ALL_VALUE ? undefined : next)}
      disabled={isEmpty || isLoading}
    >
      <SelectTrigger id={id} className={className}>
        <SelectValue placeholder={placeholder_} />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          {allowAll || allowPlatform ? (
            <SelectItem value={ALL_VALUE}>
              {allowPlatform ? t("common.platformScope") : t("common.all")}
            </SelectItem>
          ) : null}
          {apps.map((app) => (
            <SelectItem key={app.id} value={app.id as string}>
              {app.name}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  )
}
