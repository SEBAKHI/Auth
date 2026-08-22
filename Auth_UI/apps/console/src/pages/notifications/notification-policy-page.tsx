import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnDef } from "@tanstack/react-table"
import { CheckCircle2, Copy, Megaphone, Plus } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import type { Schemas } from "@authsystem/api/types"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"

import { PolicyLanguageGapNotice } from "./components/policy-language-gap-notice"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { RecordLink } from "@authsystem/ui/common/record-link"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { DatePicker, monthsFromNow } from "@authsystem/ui/common/date-picker"
import { Field, FieldGroup, FieldLabel } from "@authsystem/ui/field"
import { formatDate, formatDateTime } from "@authsystem/ui/format"
import { Textarea } from "@authsystem/ui/textarea"
import { PERMISSIONS } from "@/lib/constants"
import { policyRevisionHref } from "@/lib/record-hrefs"
import { ClonePolicyDialog } from "./components/clone-policy-dialog"
import { NotificationsTabs } from "./components/notifications-tabs"
import { PolicyVersionField } from "./components/policy-version-field"
import { Spinner } from "@authsystem/ui/spinner"

type PolicyVersionDto = Schemas["PrivacyPolicyVersionDto"]

const VERSION_RE = /^\d{4}\.\d{2}$/

/**
 * The privacy-policy revision registry: which versions exist, which one is
 * live, how complete each one's translations are, and when (and to how many
 * users) the change notice went out.
 *
 * Mirrors the notification-templates list — searchable, faceted-filterable,
 * sortable, with the version itself as the link into the editor.
 */
export function NotificationPolicyPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const canManage = hasPermission(PERMISSIONS.privacyPolicy.manage)

  const [addOpen, setAddOpen] = React.useState(false)
  const [newVersion, setNewVersion] = React.useState("")
  const [newEffectiveDate, setNewEffectiveDate] = React.useState("")
  const [newChangeNote, setNewChangeNote] = React.useState("")
  const [notifyTarget, setNotifyTarget] = React.useState<PolicyVersionDto | null>(null)
  const [publishTarget, setPublishTarget] = React.useState<PolicyVersionDto | null>(null)
  const [cloneSource, setCloneSource] = React.useState<PolicyVersionDto | null>(null)

  const versionsQuery = useQuery({
    queryKey: ["privacy-policy-versions"],
    queryFn: () => unwrap(api.GET("/api/v1/privacy-policy/versions")),
  })

  const invalidate = () =>
    void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })

  const createMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/privacy-policy/versions", {
          body: {
            version: newVersion.trim(),
            effectiveDateUtc: `${newEffectiveDate}T00:00:00Z`,
            changeNote: newChangeNote.trim() || null,
          },
        })
      ),
    onSuccess: (created) => {
      invalidate()
      setAddOpen(false)
      setNewVersion("")
      setNewEffectiveDate("")
      setNewChangeNote("")
      toast.success(t("notifications.policyCreatedToast"))
      // Straight into the editor: a version without content is not useful.
      const href = policyRevisionHref(created?.id)
      if (href) navigate(href)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const notifyMutation = useMutation({
    mutationFn: (version: string) =>
      unwrap(api.POST("/api/v1/privacy-policy/versions/notify", { body: { version } })),
    onSuccess: (result) => {
      invalidate()
      setNotifyTarget(null)
      toast.success(
        t("notifications.policyNotifiedToast", { count: result.recipientCount ?? 0 })
      )
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const publishMutation = useMutation({
    mutationFn: (version: string) =>
      unwrap(api.POST("/api/v1/privacy-policy/versions/publish", { body: { version } })),
    onSuccess: () => {
      invalidate()
      setPublishTarget(null)
      toast.success(t("notifications.policyPublishedToast"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const createValid = VERSION_RE.test(newVersion.trim()) && newEffectiveDate.length > 0

  const columns: ColumnDef<PolicyVersionDto, unknown>[] = [
    {
      id: "version",
      accessorFn: (row) => row.version ?? "",
      header: t("notifications.policyVersion"),
      meta: { label: t("notifications.policyVersion") },
      cell: ({ row }) => (
        <RecordLink
          href={policyRevisionHref(row.original.id)}
          className="min-w-0 text-start hover:underline"
        >
          {/* The direction belongs on an inline `bdi`, not on the `p`. `dir` on a
              block re-resolves the inherited `text-align: start` against that
              block's own direction, so an LTR `p` inside this RTL cell aligned
              itself left while the rest of the row stayed right. */}
          <p className="truncate font-medium">
            <bdi dir="ltr">{row.original.version}</bdi>
          </p>
          <p className="truncate text-xs text-muted-foreground">
            <bdi dir="auto">
              {row.original.changeNote || t("notifications.policyNoChangeNote")}
            </bdi>
          </p>
        </RecordLink>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => (row.isPublished ? "published" : "draft"),
      filterFn: "faceted",
      header: t("notifications.policyStatus"),
      meta: {
        label: t("notifications.policyStatus"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "published", label: t("notifications.policyPublished") },
          { value: "draft", label: t("notifications.policyDraft") },
        ],
      },
      cell: ({ row }) =>
        row.original.isPublished ? (
          <Badge>
            <CheckCircle2 data-icon="inline-start" />
            {t("notifications.policyPublished")}
          </Badge>
        ) : (
          <Badge variant="outline">{t("notifications.policyDraft")}</Badge>
        ),
    },
    {
      id: "languages",
      accessorFn: (row) => (row.languages ?? []).length,
      header: t("notifications.policyLanguages"),
      meta: { label: t("notifications.policyLanguages") },
      cell: ({ row }) => {
        const languages = row.original.languages ?? []
        return (
          <span className="text-sm text-muted-foreground" dir="ltr">
            {languages.length > 0
              ? languages.map((code) => code.toUpperCase()).join(", ")
              : "—"}
          </span>
        )
      },
    },
    {
      id: "effectiveDateUtc",
      accessorFn: (row) => row.effectiveDateUtc ?? "",
      header: t("notifications.policyEffective"),
      meta: { label: t("notifications.policyEffective") },
      cell: ({ row }) => (
        <span className="text-sm">{formatDate(row.original.effectiveDateUtc)}</span>
      ),
    },
    {
      id: "notified",
      accessorFn: (row) => (row.notifiedAtUtc ? "sent" : "not-sent"),
      filterFn: "faceted",
      header: t("notifications.policyNotifiedAt"),
      meta: {
        label: t("notifications.policyNotifiedAt"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "sent", label: t("notifications.policyNotifiedFilterSent") },
          { value: "not-sent", label: t("notifications.policyNotNotified") },
        ],
      },
      cell: ({ row }) =>
        row.original.notifiedAtUtc ? (
          <span className="text-sm">
            {formatDateTime(row.original.notifiedAtUtc)}
            <span className="ms-1 text-xs text-muted-foreground">
              ({row.original.notifiedCount ?? 0})
            </span>
          </span>
        ) : (
          <Badge variant="outline">{t("notifications.policyNotNotified")}</Badge>
        ),
    },
    ...(canManage
      ? [
          {
            id: "actions",
            header: "",
            enableSorting: false,
            enableHiding: false,
            cell: ({ row }) => (
              <div className="flex items-center justify-end gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setCloneSource(row.original)}
                >
                  <Copy data-icon="inline-start" />
                  {t("notifications.policyClone")}
                </Button>
                {row.original.isPublished ? null : (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setPublishTarget(row.original)}
                  >
                    <CheckCircle2 data-icon="inline-start" />
                    {t("notifications.policyPublish")}
                  </Button>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setNotifyTarget(row.original)}
                >
                  <Megaphone data-icon="inline-start" />
                  {t("notifications.policyNotify")}
                </Button>
              </div>
            ),
          } satisfies ColumnDef<PolicyVersionDto, unknown>,
        ]
      : []),
  ]

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
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

      <DataTable
        fillHeight
        columns={columns}
        data={versionsQuery.data ?? []}
        isLoading={versionsQuery.isLoading}
        error={versionsQuery.error}
        onRetry={() => void versionsQuery.refetch()}
        tableId="privacy-policy-versions"
        globalSearch
        searchPlaceholder={t("notifications.policySearchPlaceholder")}
        enableRowDetail={false}
        exportFileName="privacy-policy-versions"
      />

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
              <PolicyVersionField
                id="policy-version"
                value={newVersion}
                onChange={setNewVersion}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="policy-effective">
                {t("notifications.policyEffective")}
              </FieldLabel>
              <DatePicker
                id="policy-effective"
                value={newEffectiveDate}
                onChange={(value) => setNewEffectiveDate(value ?? "")}
                minDate={monthsFromNow(-5)}
                maxDate={monthsFromNow(5)}
              />
            </Field>
            <Field>
              <FieldLabel htmlFor="policy-note">
                {t("notifications.policyChangeNote")}
              </FieldLabel>
              {/* No `dir="auto"`: it resolves from the value, so an empty note
                  computed `ltr` and opened against the wrong edge in RTL. The
                  note is the admin's prose — it follows the console. */}
              <Textarea
                id="policy-note"
                rows={3}
                placeholder={t("notifications.policyChangeNoteHint")}
                value={newChangeNote}
                onChange={(event) => setNewChangeNote(event.target.value)}
              />
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button
              disabled={!createValid || createMutation.isPending}
              onClick={() => createMutation.mutate()}
            >
              {createMutation.isPending ? <Spinner /> : null}
              {t("notifications.policyAdd")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {cloneSource ? (
        <ClonePolicyDialog
          key={cloneSource.id ?? cloneSource.version}
          source={cloneSource}
          onOpenChange={(open) => {
            if (!open) setCloneSource(null)
          }}
        />
      ) : null}

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
      >
        <PolicyLanguageGapNotice languages={publishTarget?.languages} />
      </ConfirmDialog>

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
      >
        {/* The notice tells every user their policy changed, so the same gap
            matters here: readers of an untranslated language will follow that
            mail to the English document. */}
        <PolicyLanguageGapNotice languages={notifyTarget?.languages} />
      </ConfirmDialog>
    </div>
  )
}
