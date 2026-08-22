import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Ban, Check, History, Save, Send, Trash2, X } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { directionForLanguage, SUPPORTED_LANGUAGES } from "@authsystem/i18n"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  PageActionSurface,
  type PageAction,
} from "@authsystem/ui/common/page-action-surface"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Field, FieldGroup, FieldLabel, FieldTitle } from "@authsystem/ui/field"
import { formatDateTime } from "@authsystem/ui/format"
import { Input } from "@authsystem/ui/input"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { Textarea } from "@authsystem/ui/textarea"
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror"
import { PERMISSIONS } from "@/lib/constants"
import { CodeEditor } from "./components/code-editor"
import { insertAtCursor } from "./components/code-editor-utils"
import { ManageVariablesDialog } from "./components/manage-variables-dialog"
import { PublishConfirmationSummary } from "./components/publish-confirmation-summary"
import { TemplatePreview } from "./components/template-preview"
import { TestSendDialog } from "./components/test-send-dialog"
import { useSingleFlightConfirm } from "./components/use-single-flight-confirm"
import { VariablePalette } from "./components/variable-palette"
import { VersionHistorySheet } from "./components/version-history-sheet"
import {
  getRendererGlobals,
  parseVariables,
  toTranslationDrafts,
  type TranslationDraft,
} from "./lib"

const EMPTY_DRAFT: TranslationDraft = {
  subject: "",
  bodyHtml: "",
  bodyText: "",
}

interface TemplateVersionTarget {
  item: string
  applicationName: string | null
  versionId: string
  versionNumber: number | string
}

interface TemplatePublishTarget extends TemplateVersionTarget {
  revisionAt: string
}

interface TemplateDraftState {
  source: unknown
  drafts: Record<string, TranslationDraft>
  changeNote: string
}

interface TemplateSaveSnapshot {
  drafts: Record<string, TranslationDraft>
  baseline: Record<string, TranslationDraft>
  changeNote: string
  expectedModifiedAt: string | null
}

function cloneTranslationDrafts(
  drafts: Record<string, TranslationDraft>
): Record<string, TranslationDraft> {
  return Object.fromEntries(
    Object.entries(drafts).map(([language, draft]) => [
      language,
      { ...draft },
    ])
  )
}

function templateDraftMatchesSnapshot(
  draft: Pick<TemplateDraftState, "drafts" | "changeNote">,
  snapshot: TemplateSaveSnapshot
) {
  return (
    draft.changeNote === snapshot.changeNote &&
    JSON.stringify(draft.drafts) === JSON.stringify(snapshot.drafts)
  )
}

/**
 * The template editor: per-language tabs over the draft version, a variable
 * palette from the type's catalog, and a live server-rendered preview. Edits
 * accumulate locally and are saved as one draft; publishing takes the whole
 * draft (all languages) live atomically.
 */
export function NotificationTemplateDetailPage() {
  const { id = "" } = useParams()
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const editorRef = React.useRef<ReactCodeMirrorRef>(null)

  const canManage = hasPermission(PERMISSIONS.notificationTemplates.manage)
  const canPublish = hasPermission(PERMISSIONS.notificationTemplates.publish)

  const query = useQuery({
    queryKey: ["notification-template", id],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/notification-templates/{id}", {
          params: { path: { id } },
        })
      ),
    enabled: Boolean(id),
  })
  const template = query.data

  usePageBreadcrumb(template ? template.typeName : undefined)

  // State is keyed by the query object that seeded it. A refetch adopts the
  // new server snapshot without an effect-driven reset render.
  const loadedDraft = React.useMemo<TemplateDraftState>(() => {
    const source = template?.draftVersion ?? template?.publishedVersion
    return {
      source: template,
      drafts: toTranslationDrafts(source?.translations),
      changeNote: template?.draftVersion?.changeNote ?? "",
    }
  }, [template])
  const [editedDraft, setEditedDraft] = React.useState<TemplateDraftState | null>(
    null
  )
  const currentDraft =
    editedDraft !== null && editedDraft.source === template
      ? editedDraft
      : loadedDraft
  const { drafts, changeNote } = currentDraft
  const baseline = loadedDraft.drafts
  const setDrafts = (
    next: React.SetStateAction<Record<string, TranslationDraft>>
  ) =>
    setEditedDraft((current) => {
      const base =
        current !== null && current.source === template ? current : loadedDraft
      return {
        ...base,
        drafts:
          typeof next === "function" ? next(base.drafts) : next,
      }
    })
  const setChangeNote = (next: React.SetStateAction<string>) =>
    setEditedDraft((current) => {
      const base =
        current !== null && current.source === template ? current : loadedDraft
      return {
        ...base,
        changeNote:
          typeof next === "function" ? next(base.changeNote) : next,
      }
    })
  const [selectedLanguage, setActiveLanguage] = React.useState("en")
  const [historyOpen, setHistoryOpen] = React.useState(false)
  const [variablesOpen, setVariablesOpen] = React.useState(false)
  const [testSendOpen, setTestSendOpen] = React.useState(false)
  const [deleteOpen, setDeleteOpen] = React.useState(false)
  const [discardOpen, setDiscardOpen] = React.useState(false)
  const [publishTarget, setPublishTarget] =
    React.useState<TemplatePublishTarget | null>(null)
  const [unpublishTarget, setUnpublishTarget] =
    React.useState<TemplateVersionTarget | null>(null)

  const defaultLanguage = template?.defaultLanguage ?? "en"
  const activeLanguage =
    drafts[selectedLanguage] || selectedLanguage === defaultLanguage
      ? selectedLanguage
      : defaultLanguage
  const active: TranslationDraft = drafts[activeLanguage] ?? EMPTY_DRAFT
  // Direction of the translation being edited, not of the console. `dir="auto"`
  // cannot stand in for it: it resolves from the value, so an untranslated field
  // computed `ltr` and a new Arabic translation opened against the wrong edge.
  const contentDir = directionForLanguage(activeLanguage)
  const variables = parseVariables(template?.typeVariablesJson)
  const rendererGlobals = React.useMemo(() => getRendererGlobals(t), [t])

  const isDirty =
    JSON.stringify(drafts) !== JSON.stringify(baseline) ||
    changeNote !== (template?.draftVersion?.changeNote ?? "")

  const updateActive = (patch: Partial<TranslationDraft>) =>
    setDrafts((current) => ({
      ...current,
      [activeLanguage]: {
        ...(current[activeLanguage] ?? EMPTY_DRAFT),
        ...patch,
      },
    }))

  const invalidate = () => {
    void queryClient.invalidateQueries({
      queryKey: ["notification-template", id],
    })
    void queryClient.invalidateQueries({ queryKey: ["notification-templates"] })
  }

  const saveMutation = useMutation({
    mutationFn: (snapshot: TemplateSaveSnapshot) =>
      unwrap(
        api.PUT("/api/v1/notification-templates/{id}/draft", {
          params: { path: { id } },
          body: {
            translations: Object.entries(snapshot.drafts)
              .filter(
                ([, draft]) => draft.subject.trim() || draft.bodyHtml.trim()
              )
              .map(([languageCode, draft]) => ({
                languageCode,
                subject: draft.subject,
                bodyHtml: draft.bodyHtml,
                bodyText: draft.bodyText || null,
              })),
            removeLanguages: Object.entries(snapshot.baseline)
              .filter(
                ([language, previous]) =>
                  (previous.subject || previous.bodyHtml) &&
                  !(
                    snapshot.drafts[language]?.subject.trim() ||
                    snapshot.drafts[language]?.bodyHtml.trim()
                  )
              )
              .map(([language]) => language),
            changeNote: snapshot.changeNote || null,
            expectedModifiedAt: snapshot.expectedModifiedAt,
          },
        })
      ),
    onSuccess: (data, snapshot) => {
      const cached =
        queryClient.setQueryData<typeof data>(
          ["notification-template", id],
          data
        ) ?? data
      setEditedDraft((current) => {
        const latest =
          current !== null && current.source === template
            ? current
            : loadedDraft
        return templateDraftMatchesSnapshot(latest, snapshot)
          ? {
              source: cached,
              drafts: toTranslationDrafts(
                (cached.draftVersion ?? cached.publishedVersion)?.translations
              ),
              changeNote: cached.draftVersion?.changeNote ?? "",
            }
          : { ...latest, source: cached }
      })
      toast.success(t("notifications.draftSaved"))
      void queryClient.invalidateQueries({
        queryKey: ["notification-templates"],
      })
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const saveDraft = () =>
    saveMutation.mutate({
      drafts: cloneTranslationDrafts(drafts),
      baseline: cloneTranslationDrafts(baseline),
      changeNote,
      expectedModifiedAt: template?.modifiedAt ?? null,
    })

  const publishFlight = useSingleFlightConfirm()
  const unpublishFlight = useSingleFlightConfirm()

  const publishMutation = useMutation({
    mutationFn: (target: { versionId: string; revisionAt: string }) =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/publish", {
          params: { path: { id } },
          body: {
            expectedDraftVersionId: target.versionId,
            expectedRevisionAt: target.revisionAt,
          },
        })
      ),
    onSuccess: () => {
      setPublishTarget(null)
      toast.success(t("notifications.publishedToast"))
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
    onSettled: publishFlight.release,
  })

  const unpublishMutation = useMutation({
    mutationFn: (expectedPublishedVersionId: string) =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/unpublish", {
          params: { path: { id } },
          body: { expectedPublishedVersionId },
        })
      ),
    onSuccess: () => {
      setUnpublishTarget(null)
      toast.success(t("notifications.unpublishedToast"))
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
    onSettled: unpublishFlight.release,
  })

  const discardMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.DELETE("/api/v1/notification-templates/{id}/draft", {
          params: { path: { id } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.draftDiscarded"))
      setDiscardOpen(false)
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const deleteMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.DELETE("/api/v1/notification-templates/{id}", {
          params: { path: { id } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.templateDeleted"))
      void queryClient.invalidateQueries({
        queryKey: ["notification-templates"],
      })
      navigate("/notifications/templates")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const unsavedPrompt = useUnsavedChangesPrompt({
    isDirty,
    isSaving: saveMutation.isPending,
  })

  if (query.isLoading || !template) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-[540px] w-full" />
      </div>
    )
  }

  const isSystemGlobal = Boolean(
    template.typeIsSystem && !template.applicationId
  )
  const openPublishConfirmation = () => {
    if (!template.draftVersionId || !template.draftVersion) return
    const revisionAt = template.modifiedAt ?? template.createdAt
    if (!revisionAt) return
    setPublishTarget({
      item: template.typeName ?? "",
      applicationName: template.applicationName ?? null,
      versionId: template.draftVersionId,
      versionNumber: template.draftVersion.versionNumber ?? 0,
      revisionAt,
    })
  }
  const openUnpublishConfirmation = () => {
    if (!template.publishedVersionId || !template.publishedVersion) return
    setUnpublishTarget({
      item: template.typeName ?? "",
      applicationName: template.applicationName ?? null,
      versionId: template.publishedVersionId,
      versionNumber: template.publishedVersion.versionNumber ?? 0,
    })
  }
  const templateActions: PageAction[] = [
    ...(canManage
      ? [
          {
            id: "save",
            label: t("notifications.saveDraft"),
            icon: Save,
            disabled: !isDirty,
            pending: saveMutation.isPending,
            onAction: saveDraft,
          },
          {
            id: "test-send",
            label: t("notifications.testSend"),
            icon: Send,
            onAction: () => setTestSendOpen(true),
          },
        ]
      : []),
    ...(canPublish
      ? [
          {
            id: "publish",
            label: t("notifications.publish"),
            icon: Check,
            variant: "default" as const,
            disabled: !template.draftVersionId || isDirty,
            pending: publishMutation.isPending,
            onAction: openPublishConfirmation,
          },
        ]
      : []),
    {
      id: "history",
      label: t("notifications.versionHistory"),
      icon: History,
      onAction: () => setHistoryOpen(true),
    },
    ...(canPublish && template.publishedVersionId && !isSystemGlobal
      ? [
          {
            id: "unpublish",
            label: t("notifications.unpublish"),
            icon: Ban,
            pending: unpublishMutation.isPending,
            onAction: openUnpublishConfirmation,
          },
        ]
      : []),
    ...(canManage && template.draftVersionId
      ? [
          {
            id: "discard",
            label: t("notifications.discardDraft"),
            icon: X,
            variant: "destructive" as const,
            pending: discardMutation.isPending,
            onAction: () => setDiscardOpen(true),
          },
        ]
      : []),
    ...(canManage && !isSystemGlobal
      ? [
          {
            id: "delete",
            label: t("common.delete"),
            icon: Trash2,
            variant: "destructive" as const,
            pending: deleteMutation.isPending,
            onAction: () => setDeleteOpen(true),
          },
        ]
      : []),
  ]

  return (
    // From `xl` the page fills the shell's height and the editor/preview pair
    // below scrolls per column, so the preview stays in view while the body is
    // being edited. Narrower than that the two stack and the page scrolls once.
    <div className="flex flex-col gap-6 xl:min-h-0 xl:flex-1">
      <PageHeader
        title={template.typeName ?? ""}
        description={
          template.applicationName
            ? t("notifications.appScopeDescription", {
                application: template.applicationName,
              })
            : t("notifications.globalScopeDescription")
        }
        actions={
          <PageActionSurface
            actions={templateActions}
            label={t("common.actions")}
          />
        }
      />

      <div className="flex flex-wrap items-center gap-2">
        {template.publishedVersion ? (
          <Badge>
            {t("notifications.publishedVersion", {
              version: template.publishedVersion.versionNumber ?? 0,
            })}
          </Badge>
        ) : (
          <Badge variant="outline">{t("notifications.unpublished")}</Badge>
        )}
        {template.draftVersion ? (
          <Badge variant="secondary">
            {t("notifications.draftVersion", {
              version: template.draftVersion.versionNumber ?? 0,
            })}
          </Badge>
        ) : null}
        {isSystemGlobal ? (
          <Badge variant="destructive">{t("notifications.systemBadge")}</Badge>
        ) : null}
        {isDirty ? (
          <Badge variant="outline">{t("notifications.unsavedChanges")}</Badge>
        ) : null}
      </div>

      {isSystemGlobal ? (
        <p className="text-sm text-muted-foreground">
          {t("notifications.systemTypeHint")}
        </p>
      ) : null}

      {/* A flex row rather than a two-column grid: `flex-1` splits the width
          exactly as `grid-cols-2` did, and only a flex child can be told to
          shrink below its content and scroll. */}
      <div className="flex flex-col gap-6 xl:min-h-0 xl:flex-1 xl:flex-row">
        <div className="flex flex-col gap-4 xl:min-h-0 xl:min-w-0 xl:flex-1 xl:overflow-y-auto">
          <Tabs value={activeLanguage} onValueChange={setActiveLanguage}>
            {/* Wrapping needs the height to follow the rows; the strip's
                default fixed height would cut off every row but the first. */}
            <TabsList className="h-auto! flex-wrap">
              {SUPPORTED_LANGUAGES.map((language) => {
                const draft = drafts[language.code]
                const filled = Boolean(
                  draft?.subject.trim() || draft?.bodyHtml.trim()
                )
                return (
                  <TabsTrigger key={language.code} value={language.code}>
                    <span dir="ltr" className="uppercase">
                      {language.code}
                    </span>
                    {filled ? <Check className="size-3" /> : null}
                  </TabsTrigger>
                )
              })}
            </TabsList>
          </Tabs>

          <FieldGroup>
            <Field data-disabled={!canManage}>
              <FieldLabel htmlFor="template-subject">
                {t("notifications.subject")}
              </FieldLabel>
              <Input
                id="template-subject"
                dir={contentDir}
                value={active.subject}
                onChange={(e) => updateActive({ subject: e.target.value })}
                placeholder={t("notifications.subjectPlaceholder")}
                disabled={!canManage}
              />
            </Field>

            {/* The editor is a CodeMirror surface, not a labelable control, so
                the field is titled and the editor carries the same aria-label. */}
            <Field>
              <FieldTitle>{t("notifications.bodyHtml")}</FieldTitle>
              <CodeEditor
                ref={editorRef}
                value={active.bodyHtml}
                onChange={(value) => updateActive({ bodyHtml: value })}
                ariaLabel={t("notifications.bodyHtml")}
                allowImages
                contentDir={contentDir}
                readOnly={!canManage}
              />
            </Field>

            <div className="flex flex-col gap-4">
              <VariablePalette
                variables={variables}
                onInsert={
                  canManage
                    ? (placeholder) => {
                        if (!insertAtCursor(editorRef, placeholder)) {
                          updateActive({
                            bodyHtml: active.bodyHtml + placeholder,
                          })
                        }
                      }
                    : undefined
                }
              />
              <VariablePalette
                title={t("notifications.globalVariables")}
                variables={rendererGlobals}
                onInsert={
                  canManage
                    ? (placeholder) => {
                        if (!insertAtCursor(editorRef, placeholder)) {
                          updateActive({
                            bodyHtml: active.bodyHtml + placeholder,
                          })
                        }
                      }
                    : undefined
                }
              />
              {canManage ? (
                // `self-start` because the parent is now `flex flex-col`: under the
                // old `space-y-4` this button was a block-level sibling and hugged
                // its label, but a flex item stretches to the container width.
                <Button
                  variant="ghost"
                  size="sm"
                  className="self-start"
                  onClick={() => setVariablesOpen(true)}
                >
                  {t("notifications.manageVariables")}
                </Button>
              ) : null}
            </div>

            <Field data-disabled={!canManage}>
              <FieldLabel htmlFor="template-body-text">
                {t("notifications.bodyText")}
              </FieldLabel>
              <Textarea
                id="template-body-text"
                dir={contentDir}
                rows={4}
                value={active.bodyText}
                onChange={(e) => updateActive({ bodyText: e.target.value })}
                placeholder={t("notifications.bodyTextHint")}
                disabled={!canManage}
              />
            </Field>

            <Field data-disabled={!canManage}>
              <FieldLabel htmlFor="template-change-note">
                {t("notifications.changeNote")}
              </FieldLabel>
              {/* The change note describes the revision for other admins; it is
                  not part of any translation, so it follows the console. */}
              <Input
                id="template-change-note"
                value={changeNote}
                onChange={(e) => setChangeNote(e.target.value)}
                placeholder={t("notifications.changeNotePlaceholder")}
                disabled={!canManage}
              />
            </Field>
          </FieldGroup>
        </div>

        {/* The preview is a bare row child, so the scroll column is this
            wrapper rather than the component itself. */}
        <div className="xl:min-h-0 xl:min-w-0 xl:flex-1 xl:overflow-y-auto">
          <TemplatePreview
            notificationTypeId={template.notificationTypeId!}
            applicationId={template.applicationId}
            languageCode={activeLanguage}
            subject={active.subject}
            bodyHtml={active.bodyHtml}
            bodyText={active.bodyText}
          />
        </div>
      </div>

      <VersionHistorySheet
        open={historyOpen}
        onOpenChange={setHistoryOpen}
        template={template}
        canPublish={canPublish}
        canManage={canManage}
      />

      {variablesOpen ? (
        <ManageVariablesDialog
          open
          onOpenChange={setVariablesOpen}
          template={template}
        />
      ) : null}

      <TestSendDialog
        open={testSendOpen}
        onOpenChange={setTestSendOpen}
        templateId={id}
        defaultLanguage={template.defaultLanguage ?? "en"}
      />

      <ConfirmDialog
        open={publishTarget !== null}
        onOpenChange={(open) => {
          if (!open && !publishMutation.isPending) setPublishTarget(null)
        }}
        title={t("notifications.publishConfirmTitle", {
          item: publishTarget?.item ?? "",
        })}
        description={t("notifications.publishConfirmBody")}
        confirmLabel={t("notifications.publish")}
        loading={publishMutation.isPending}
        onConfirm={() => {
          if (publishTarget) {
            publishFlight.run(() => {
              publishMutation.mutate({
                versionId: publishTarget.versionId,
                revisionAt: publishTarget.revisionAt,
              })
            })
          }
        }}
      >
        {publishTarget ? (
          <PublishConfirmationSummary
            item={publishTarget.item}
            revision={`${t("notifications.draftVersion", {
              version: publishTarget.versionNumber,
            })} · ${formatDateTime(publishTarget.revisionAt)}`}
            scope={
              publishTarget.applicationName ?? t("notifications.globalTemplate")
            }
          />
        ) : null}
      </ConfirmDialog>

      <ConfirmDialog
        open={unpublishTarget !== null}
        onOpenChange={(open) => {
          if (!open && !unpublishMutation.isPending) setUnpublishTarget(null)
        }}
        title={t("notifications.unpublishConfirmTitle", {
          item: unpublishTarget?.item ?? "",
        })}
        description={t("notifications.unpublishConfirmBody")}
        confirmLabel={t("notifications.unpublish")}
        destructive
        loading={unpublishMutation.isPending}
        onConfirm={() => {
          if (unpublishTarget) {
            unpublishFlight.run(() => {
              unpublishMutation.mutate(unpublishTarget.versionId)
            })
          }
        }}
      >
        {unpublishTarget ? (
          <PublishConfirmationSummary
            item={unpublishTarget.item}
            revision={t("notifications.publishedVersion", {
              version: unpublishTarget.versionNumber,
            })}
            scope={
              unpublishTarget.applicationName ??
              t("notifications.globalTemplate")
            }
          />
        ) : null}
      </ConfirmDialog>

      <ConfirmDialog
        open={discardOpen}
        onOpenChange={setDiscardOpen}
        title={t("notifications.discardDraftTitle")}
        description={t("notifications.discardDraftBody")}
        confirmLabel={t("notifications.discardDraft")}
        destructive
        loading={discardMutation.isPending}
        onConfirm={() => discardMutation.mutate()}
      />

      <ConfirmDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title={t("notifications.deleteTitle")}
        description={t("notifications.deleteBody")}
        confirmLabel={t("common.delete")}
        destructive
        loading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
      />
      {unsavedPrompt}
    </div>
  )
}
