import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"

import { RecordLink } from "@authsystem/ui/common/record-link"
import { Field, FieldDescription, FieldLabel } from "@authsystem/ui/field"
import { formatDateTime } from "@authsystem/ui/format"
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemTitle,
} from "@authsystem/ui/item"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"

import { organizationHref } from "@/lib/record-hrefs"

import { useAssignmentScopeLabel } from "./scoped-assignment-key"

/**
 * The application roles a user holds through organizations, read-only.
 *
 * Removing a role from the user's own list leaves the same role in that
 * application's tokens when an organization also gives it; the page said "saved"
 * and showed nothing else. These rows name the organization that gives each one
 * and link to its page, where it is removed: never a remove control here, and
 * never mixed into the user's own removable list.
 *
 * Shows nothing while loading, when there are none, and when the read fails:
 * an older API has no such endpoint (404), and the user's own roles above are
 * unaffected either way.
 */
export function OrganizationHeldRoles({
  userId,
  enabled = true,
}: {
  userId: string
  enabled?: boolean
}) {
  const { t } = useTranslation()
  const scopeLabel = useAssignmentScopeLabel()

  const query = useQuery({
    queryKey: ["users", userId, "organization-roles"],
    enabled,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/organization-roles", {
          params: { path: { id: userId } },
        })
      ),
  })

  const rows = query.data ?? []
  if (rows.length === 0) return null

  return (
    <Field>
      <FieldLabel>{t("users.organizationRoles.title")}</FieldLabel>
      <FieldDescription>
        {t("users.organizationRoles.description")}
      </FieldDescription>
      <ItemGroup>
        {rows.map((row) => (
          <Item
            key={`${row.organizationId}:${row.applicationId}:${row.roleId}`}
            role="listitem"
            size="sm"
            variant="outline"
          >
            <ItemContent>
              <ItemTitle>
                {`${row.roleName as string} (${scopeLabel(row) ?? ""})`}
              </ItemTitle>
              {row.expiresAt ? (
                <ItemDescription>
                  {t("users.organizationRoles.expires", {
                    date: formatDateTime(row.expiresAt),
                  })}
                </ItemDescription>
              ) : null}
            </ItemContent>
            <ItemActions>
              <RecordLink href={organizationHref(row.organizationId)}>
                {row.organizationName}
              </RecordLink>
            </ItemActions>
          </Item>
        ))}
      </ItemGroup>
    </Field>
  )
}
