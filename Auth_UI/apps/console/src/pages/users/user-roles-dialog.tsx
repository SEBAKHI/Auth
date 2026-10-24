import { useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import {
  AssignmentDialog,
  AssignmentPicker,
} from "@authsystem/ui/common/assignment-dialog"
import { SearchableSelect } from "@authsystem/ui/common/searchable-select"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

import {
  parseScopedAssignmentKey,
  pickedAssignmentKey,
  scopedAssignmentKey,
} from "./scoped-assignment-key"

interface RoleDraft {
  roleId: string
  expiresAt: string | null
}

export function UserRolesDialog({
  open,
  onOpenChange,
  user,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: Schemas["UserDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const userId = user.id as string
  const [selectedRole, setSelectedRole] = React.useState<string>()
  const [expiresAt, setExpiresAt] = React.useState("")

  const rolesQuery = useQuery({
    queryKey: ["users", userId, "roles"],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/roles", {
          params: { path: { id: userId } },
        })
      ),
  })

  const allRolesQuery = useQuery({
    queryKey: ["roles", "all"],
    enabled: open,
    queryFn: () => unwrap(api.GET("/api/v1/Roles", { params: { query: {} } })),
  })

  const roleLabel = (role: Schemas["RoleDto"]) =>
    `${role.name}${
      role.applicationName
        ? ` (${role.applicationName})`
        : role.applicationId
          ? ""
          : ` (${t("nav.platform")})`
    }`

  // One chip per assignment, and its application named: the same role can be
  // held platform-wide and inside an application, and the chip says which one
  // its ✕ removes. The roles tab reads the same field for its column. The ✕
  // shows only where the API would accept the removal from this administrator;
  // a response without the answer (an older API) keeps it.
  const items = (rolesQuery.data ?? []).map((role) => {
    const scope = role.applicationName ?? role.applicationCode
    return {
      key: scopedAssignmentKey(role.roleId as string, role.applicationId),
      label: scope
        ? `${role.roleName as string} (${scope})`
        : (role.roleName as string),
      removable: role.canRemove !== false,
    }
  })

  return (
    <AssignmentDialog<RoleDraft>
      open={open}
      onOpenChange={onOpenChange}
      title={t("users.manageRoles")}
      description={user.email}
      items={items}
      loading={rolesQuery.isLoading}
      emptyLabel={t("users.noRoles")}
      assignedLabel={t("users.manageRoles")}
      picker={({ assignedKeys, add }) => {
        // Offered while not assigned at the scope the server writes it at (an
        // application's role inside its application), and only when the server
        // says this administrator may assign it: a role carrying more than they
        // hold, or an application's role without applications:update, is
        // refused after the choice. Absent (an older API) keeps every role.
        const available = (allRolesQuery.data ?? []).filter(
          (role): role is Schemas["RoleDto"] & { id: string } =>
            Boolean(role.id) &&
            role.canGrant !== false &&
            !assignedKeys.has(
              pickedAssignmentKey({
                id: role.id as string,
                applicationId: role.applicationId,
              })
            )
        )

        return (
          <AssignmentPicker
            addLabel={t("users.assignRole")}
            canAdd={Boolean(selectedRole)}
            expiresAt={expiresAt}
            onExpiresAtChange={setExpiresAt}
            onAdd={() => {
              const role = available.find((item) => item.id === selectedRole)
              if (!role) return
              add({
                key: pickedAssignmentKey(role),
                label: roleLabel(role),
                draft: {
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
            {/* Searchable for the same reason the permission picker is: the
                list grows with every role anyone defines, and a Select offers
                no way to reach one except scrolling. Role names follow the
                interface language, so no direction override here. */}
            <SearchableSelect
              value={selectedRole}
              options={available.map((role) => ({
                id: role.id,
                label: roleLabel(role),
                description: role.description ?? undefined,
              }))}
              onChange={setSelectedRole}
              placeholder={t("users.assignRole")}
            />
          </AssignmentPicker>
        )
      }}
      onAdd={async (draft) => {
        const { error } = await api.POST("/api/v1/Users/{id}/roles", {
          params: { path: { id: userId } },
          body: { roleId: draft.roleId, expiresAt: draft.expiresAt },
        })
        if (error) throw error
      }}
      onRemove={async (key) => {
        const { id: roleId, applicationId } = parseScopedAssignmentKey(key)
        const { error } = await api.DELETE(
          "/api/v1/Users/{id}/roles/{roleId}",
          {
            params: {
              path: { id: userId, roleId },
              query: applicationId ? { applicationId } : undefined,
            },
          }
        )
        if (error) throw error
      }}
      onApplied={() => {
        void queryClient.invalidateQueries({
          queryKey: ["users", userId, "roles"],
        })
        void queryClient.invalidateQueries({ queryKey: ["users"] })
      }}
    />
  )
}
