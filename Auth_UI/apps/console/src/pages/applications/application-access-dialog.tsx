import { useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import {
  AssignmentDialog,
  AssignmentPicker,
} from "@authsystem/ui/common/assignment-dialog"
import { UserSelect } from "@authsystem/ui/common/user-select"
import { Field, FieldLabel } from "@authsystem/ui/field"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { fullName } from "@authsystem/ui/format"

interface AccessDraft {
  userId: string
  roleId: string | null
  expiresAt: string | null
}

/** Sentinel for "no role", since a Select item cannot carry an empty value. */
const NO_ROLE = "__none__"

/**
 * The access list of a restricted application: who may sign in, and optionally
 * with which role.
 *
 * The role is offered here rather than only on each invitee's own page because
 * the invitation on its own grants no authority — a trial user admitted without
 * one can sign in and do nothing, and walking five people's pages to fix that is
 * the workflow this dialog exists to avoid.
 */
export function ApplicationAccessDialog({
  open,
  onOpenChange,
  applicationId,
  applicationName,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  applicationId: string
  applicationName: string
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [selectedUser, setSelectedUser] = React.useState<string>()
  // Kept alongside the id so a staged row reads as a person, not a GUID.
  const [selectedUserLabel, setSelectedUserLabel] = React.useState("")
  const [selectedRole, setSelectedRole] = React.useState<string>(NO_ROLE)
  const [expiresAt, setExpiresAt] = React.useState("")

  const grantsQuery = useQuery({
    queryKey: ["applications", applicationId, "access"],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/access", {
          params: { path: { id: applicationId } },
        })
      ),
  })

  const rolesQuery = useQuery({
    queryKey: ["applications", applicationId, "roles"],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/roles", {
          params: { path: { id: applicationId } },
        })
      ),
  })

  const grants = grantsQuery.data ?? []

  const items = grants.map((grant) => ({
    key: grant.userId as string,
    label:
      fullName(grant.firstName, grant.lastName) ||
      grant.displayName ||
      (grant.email as string),
  }))

  const assigned = new Set(items.map((item) => item.key))

  return (
    <AssignmentDialog<AccessDraft>
      open={open}
      onOpenChange={onOpenChange}
      title={t("applications.manageAccess")}
      description={t("applications.manageAccessDescription", {
        name: applicationName,
      })}
      items={items}
      loading={grantsQuery.isLoading}
      emptyLabel={t("applications.noAccessGrants")}
      assignedLabel={t("applications.manageAccess")}
      picker={({ assignedKeys, add }) => (
        <AssignmentPicker
          addLabel={t("applications.grantAccess")}
          canAdd={Boolean(selectedUser)}
          expiresAt={expiresAt}
          onExpiresAtChange={setExpiresAt}
          onAdd={() => {
            if (!selectedUser) return
            const role = (rolesQuery.data ?? []).find(
              (item) => item.id === selectedRole
            )
            add({
              key: selectedUser,
              label: role
                ? `${selectedUserLabel} — ${role.name}`
                : selectedUserLabel,
              draft: {
                userId: selectedUser,
                roleId: selectedRole === NO_ROLE ? null : selectedRole,
                expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
              },
            })
            setSelectedUser(undefined)
            setSelectedUserLabel("")
            setSelectedRole(NO_ROLE)
            setExpiresAt("")
          }}
        >
          <Field>
            <FieldLabel htmlFor="access-user">
              {t("applications.accessUser")}
            </FieldLabel>
            <UserSelect
              id="access-user"
              value={selectedUser}
              onChange={(userId, label) => {
                setSelectedUser(userId)
                setSelectedUserLabel(label ?? "")
              }}
              excludeIds={new Set([...assignedKeys, ...assigned])}
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="access-role">
              {t("applications.accessRole")}
            </FieldLabel>
            <Select value={selectedRole} onValueChange={setSelectedRole}>
              <SelectTrigger id="access-role" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem value={NO_ROLE}>{t("common.none")}</SelectItem>
                  {(rolesQuery.data ?? []).map((role) => (
                    <SelectItem key={role.id} value={role.id as string}>
                      {role.name}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
        </AssignmentPicker>
      )}
      onAdd={async (draft) => {
        const { error } = await api.POST("/api/v1/Applications/{id}/access", {
          params: { path: { id: applicationId } },
          body: {
            userId: draft.userId,
            roleId: draft.roleId,
            expiresAt: draft.expiresAt,
          },
        })
        if (error) throw error
      }}
      onRemove={async (userId) => {
        const { error } = await api.DELETE(
          "/api/v1/Applications/{id}/access/{userId}",
          { params: { path: { id: applicationId, userId } } }
        )
        if (error) throw error
      }}
      onApplied={() => {
        void queryClient.invalidateQueries({
          queryKey: ["applications", applicationId, "access"],
        })
        // The Users tab counts invitations too, so it must be refreshed with it.
        void queryClient.invalidateQueries({ queryKey: ["app-users"] })
      }}
    />
  )
}
