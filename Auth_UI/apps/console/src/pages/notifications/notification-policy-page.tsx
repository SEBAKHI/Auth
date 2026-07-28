import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { CheckCircle2, FileText, Loader2, Megaphone, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import type { Schemas } from "@astoom/api/types"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { Card, CardContent } from "@astoom/ui/card"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { PageHeader } from "@astoom/ui/common/page-header"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { Field, FieldGroup, FieldLabel } from "@astoom/ui/field"
import { formatDate, formatDateTime } from "@astoom/ui/format"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"
import { PERMISSIONS } from "@/lib/constants"
import { NotificationsTabs } from "./components/notifications-tabs"
import { PolicyContentEditor } from "./components/policy-content-editor"

type PolicyVersionDto = Schemas["PrivacyPolicyVersionDto"]

const VERSION_RE = /^\d{4}\.\d{2}$/

/**
 * The privacy-policy revision registry: which versions exist, when each took
 * effect, and when (and to how many users) the change notice went out. The
 * notify action is the compliance record behind the published policy's "we
 * notify you of material changes" promise — it emails every active,
 * confirmed account from the privacy-policy-updated template, each user in
 * their preferred language.
 */
export function NotificationPolicyPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()

  const canManage = hasPermission(PERMISSIONS.privacyPolicy.manage)

  const [addOpen, setAddOpen] = React.useState(false)
  const [newVersion, setNewVersion] = React.useState("")
  const [newEffectiveDate, setNewEffectiveDate] = React.useState("")
  const [notifyTarget, setNotifyTarget] = React.useState<PolicyVersionDto | null>(null)
  const [publishTarget, setPublishTarget] = React.useState<PolicyVersionDto | null>(null)
  const [editingVersion, setEditingVersion] = React.useState<string | null>(null)

  const versionsQuery = useQuery({
    queryKey: ["privacy-policy-versions"],
    queryFn: () => unwrap(api.GET("/api/v1/privacy-policy/versions")),
  })

  const createMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/privacy-policy/versions", {
          body: {
            version: newVersion.trim(),
            effectiveDateUtc: `${newEffectiveDate}T00:00:00Z`,
          },
        })
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      setAddOpen(false)
      setNewVersion("")
      setNewEffectiveDate("")
      toast.success(t("notifications.policyCreatedToast"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const notifyMutation = useMutation({
    mutationFn: (version: string) =>
      unwrap(
        api.POST("/api/v1/privacy-policy/versions/notify", {
          body: { version },
        })
      ),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      setNotifyTarget(null)
      toast.success(
        t("notifications.policyNotifiedToast", { count: result.recipientCount ?? 0 })
      )
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const publishMutation = useMutation({
    mutationFn: (version: string) =>
      unwrap(
        api.POST("/api/v1/privacy-policy/versions/publish", {
          body: { version },
        })
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      setPublishTarget(null)
      toast.success(t("notifications.policyPublishedToast"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const createValid =
    VERSION_RE.test(newVersion.trim()) && newEffectiveDate.length > 0

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("notifications.policyTitle")}
        description={t("notifications.policySubtitle")}
        actions={
          canManage ? (
            <Button onClick={() => setAddOpen(true)}>
              <Plus data-icon="inline-start" />
              {t("notifications.policyAdd")}
            </Button>
          ) : undefined
        }
      />

      <NotificationsTabs />

      {versionsQuery.isLoading || !versionsQuery.data ? (
        <Skeleton className="h-48 w-full" />
      ) : (
        <Card>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("notifications.policyVersion")}</TableHead>
                  <TableHead>{t("notifications.policyStatus")}</TableHead>
                  <TableHead>{t("notifications.policyLanguages")}</TableHead>
                  <TableHead>{t("notifications.policyEffective")}</TableHead>
                  <TableHead>{t("notifications.policyNotifiedAt")}</TableHead>
                  <TableHead>{t("notifications.policyRecipients")}</TableHead>
                  {canManage ? <TableHead /> : null}
                </TableRow>
              </TableHeader>
              <TableBody>
                {versionsQuery.data.map((version) => (
                  <TableRow key={version.id}>
                    <TableCell className="font-medium">{version.version}</TableCell>
                    <TableCell>
                      {version.isPublished ? (
                        <Badge>
                          <CheckCircle2 data-icon="inline-start" />
                          {t("notifications.policyPublished")}
                        </Badge>
                      ) : (
                        <Badge variant="outline">
                          {t("notifications.policyDraft")}
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {(version.languages ?? []).length > 0
                        ? (version.languages ?? [])
                            .map((code) => code.toUpperCase())
                            .join(", ")
                        : "—"}
                    </TableCell>
                    <TableCell>{formatDate(version.effectiveDateUtc)}</TableCell>
                    <TableCell>
                      {version.notifiedAtUtc ? (
                        formatDateTime(version.notifiedAtUtc)
                      ) : (
                        <Badge variant="outline">
                          {t("notifications.policyNotNotified")}
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell>{version.notifiedCount ?? "—"}</TableCell>
                    {canManage ? (
                      <TableCell className="text-end">
                        <div className="flex items-center justify-end gap-2">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setEditingVersion(version.version ?? null)}
                          >
                            <FileText data-icon="inline-start" />
                            {t("notifications.policyEditContent")}
                          </Button>
                          {version.isPublished ? null : (
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => setPublishTarget(version)}
                            >
                              <CheckCircle2 data-icon="inline-start" />
                              {t("notifications.policyPublish")}
                            </Button>
                          )}
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setNotifyTarget(version)}
                          >
                            <Megaphone data-icon="inline-start" />
                            {t("notifications.policyNotify")}
                          </Button>
                        </div>
                      </TableCell>
                    ) : null}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      <Dialog open={addOpen} onOpenChange={setAddOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("notifications.policyAddTitle")}</DialogTitle>
            <DialogDescription>
              {t("notifications.policyAddDescription")}
            </DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="policy-version">
                {t("notifications.policyVersion")}
              </FieldLabel>
              <Input
                id="policy-version"
                placeholder="2026.07"
                value={newVersion}
                onChange={(event) => setNewVersion(event.target.value)}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="policy-effective">
                {t("notifications.policyEffective")}
              </FieldLabel>
              <Input
                id="policy-effective"
                type="date"
                value={newEffectiveDate}
                onChange={(event) => setNewEffectiveDate(event.target.value)}
              />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button
              disabled={!createValid || createMutation.isPending}
              onClick={() => createMutation.mutate()}
            >
              {createMutation.isPending ? <Loader2 className="animate-spin" /> : null}
              {t("notifications.policyAdd")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <PolicyContentEditor
        version={editingVersion ?? ""}
        open={editingVersion !== null}
        onOpenChange={(open) => {
          if (!open) setEditingVersion(null)
        }}
        canManage={canManage}
      />

      <ConfirmDialog
        open={publishTarget !== null}
        onOpenChange={(open) => {
          if (!open) setPublishTarget(null)
        }}
        title={t("notifications.policyPublishTitle")}
        description={t("notifications.policyPublishBody")}
        confirmLabel={t("notifications.policyPublish")}
        loading={publishMutation.isPending}
        onConfirm={() => {
          if (publishTarget?.version) publishMutation.mutate(publishTarget.version)
        }}
      />

      <ConfirmDialog
        open={notifyTarget !== null}
        onOpenChange={(open) => {
          if (!open) setNotifyTarget(null)
        }}
        title={t("notifications.policyNotifyTitle")}
        description={t("notifications.policyNotifyBody")}
        confirmLabel={t("notifications.policyNotify")}
        loading={notifyMutation.isPending}
        onConfirm={() => {
          if (notifyTarget?.version) notifyMutation.mutate(notifyTarget.version)
        }}
      />
    </div>
  )
}
