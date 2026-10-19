import { useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import {
  AssignmentDialog,
  AssignmentPicker,
} from "@authsystem/ui/common/assignment-dialog"
import { FieldDescription, FieldError } from "@authsystem/ui/field"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { enabledApplications } from "./invite-link"
import {
  useAssignableAppRoles,
  useOrganizationApplications,
} from "./role-options"

interface AppRoleDraft {
  applicationId: string
  roleId: string
  expiresAt: string | null
}

/**
 * Manages a member's app-scoped role assignments within an organization.
 * Edits are staged and applied together — see `AssignmentDialog`.
 */
export function MemberAppRolesDialog({
  open,
  onOpenChange,
  orgId,
  member,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  orgId: string
  member: Schemas["OrganizationMemberDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const userId = member.userId as string
  const [selectedApp, setSelectedApp] = React.useState<string>()
  const [selectedRole, setSelectedRole] = React.useState<string>()
  const [expiresAt, setExpiresAt] = React.useState("")

  const assignmentsQuery = useQuery({
    queryKey: ["org-member-app-roles", orgId, userId],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{orgId}/members/{userId}/roles", {
          params: { path: { orgId, userId } },
        })
      ),
  })

  const appsQuery = useOrganizationApplications(orgId, open)

  // Only the roles this caller may assign, decided by the server with the same
  // check the assignment runs, so nothing offered here is refused on Apply.
  const rolesQuery = useAssignableAppRoles(orgId, selectedApp, open)
  const noAssignableRoles = rolesQuery.isSuccess && rolesQuery.data.length === 0

  const enabledApps = enabledApplications(appsQuery.data)

  const items = (assignmentsQuery.data ?? []).map((assignment) => ({
    key: assignment.roleId as string,
    label: `${assignment.applicationName} · ${assignment.roleName}`,
  }))

  return (
    <AssignmentDialog<AppRoleDraft>
      open={open}
      onOpenChange={onOpenChange}
      title={t("organizations.manageAppRoles")}
      description={member.email}
      items={items}
      loading={assignmentsQuery.isLoading}
      emptyLabel={t("organizations.noAppRoles")}
      assignedLabel={t("organizations.manageAppRoles")}
      picker={({ assignedKeys, add }) => {
        const availableRoles = (rolesQuery.data ?? []).filter(
          (role) => role.id && !assignedKeys.has(role.id)
        )
        const application = enabledApps.find(
          (app) => app.applicationId === selectedApp
        )

        return (
          <AssignmentPicker
            addLabel={t("organizations.assignAppRole")}
            canAdd={Boolean(selectedApp && selectedRole)}
            expiresAt={expiresAt}
            onExpiresAtChange={setExpiresAt}
            onAdd={() => {
              const role = availableRoles.find(
                (item) => item.id === selectedRole
              )
              if (!role?.id || !selectedApp) return
              add({
                key: role.id,
                label: `${application?.applicationName} · ${role.name}`,
                draft: {
                  applicationId: selectedApp,
                  roleId: role.id,
                  expiresAt: expiresAt
                    ? new Date(expiresAt).toISOString()
                    : null,
                },
              })
              setSelectedRole(undefined)
              setExpiresAt("")
            }}
          >
            <Select
              value={selectedApp}
              onValueChange={(value) => {
                setSelectedApp(value)
                setSelectedRole(undefined)
              }}
            >
              <SelectTrigger className="w-full">
                <SelectValue
                  placeholder={t("organizations.selectApplication")}
                />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {enabledApps.map((app) => (
                    <SelectItem
                      key={app.applicationId}
                      value={app.applicationId as string}
                    >
                      {app.applicationName}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
            <Select
              value={selectedRole}
              onValueChange={setSelectedRole}
              disabled={!selectedApp || noAssignableRoles}
            >
              <SelectTrigger
                className="w-full"
                aria-invalid={rolesQuery.isError || appsQuery.isError}
              >
                <SelectValue placeholder={t("organizations.selectRole")} />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {availableRoles.map((role) => (
                    <SelectItem key={role.id} value={role.id as string}>
                      {role.name}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
            {noAssignableRoles ? (
              <FieldDescription>
                {t("organizations.noAssignableAppRoles")}
              </FieldDescription>
            ) : null}
            {appsQuery.isError || rolesQuery.isError ? (
              <FieldError>
                {getErrorMessage(appsQuery.error ?? rolesQuery.error)}
              </FieldError>
            ) : null}
          </AssignmentPicker>
        )
      }}
      onAdd={async (draft) => {
        const { error } = await api.POST(
          "/api/v1/Organizations/{orgId}/members/{userId}/roles",
          {
            params: { path: { orgId, userId } },
            body: {
              applicationId: draft.applicationId,
              roleId: draft.roleId,
              expiresAt: draft.expiresAt,
            },
          }
        )
        if (error) throw error
      }}
      onRemove={async (roleId) => {
        const { error } = await api.DELETE(
          "/api/v1/Organizations/{orgId}/members/{userId}/roles/{roleId}",
          { params: { path: { orgId, userId, roleId } } }
        )
        if (error) throw error
      }}
      onApplied={() => {
        void queryClient.invalidateQueries({
          queryKey: ["org-member-app-roles", orgId, userId],
        })
        // Assignment changes move the applications tab's assigned-user counts.
        void queryClient.invalidateQueries({ queryKey: ["org-apps", orgId] })
      }}
    />
  )
}
