import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Check, History, MoreHorizontal, Send } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { PageHeader } from "@astoom/ui/common/page-header"
import { usePageBreadcrumb } from "@astoom/ui/crumbs"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { Field, FieldGroup, FieldLabel, FieldTitle } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { Textarea } from "@astoom/ui/textarea"
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror"
import { PERMISSIONS } from "@/lib/constants"
import { CodeEditor, insertAtCursor } from "./components/code-editor"
import { ManageVariablesDialog } from "./components/manage-variables-dialog"
import { TemplatePreview } from "./components/template-preview"
import { TestSendDialog } from "./components/test-send-dialog"
import { VariablePalette } from "./components/variable-palette"
import { VersionHistorySheet } from "./components/version-history-sheet"
import {
  getRendererGlobals,
  parseVariables,
  toTranslationDrafts,
  type TranslationDraft,
} from "./lib"

const EMPTY_DRAFT: TranslationDraft = { subject: "", bodyHtml: "", bodyText: "" }

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

  // Local editing state: seeded from the draft version when one exists, else
  // from the published version (the first save creates the draft server-side).
  const [drafts, setDrafts] = React.useState<Record<string, TranslationDraft>>({})
  const [baseline, setBaseline] = React.useState<Record<string, TranslationDraft>>({})
  const [activeLanguage, setActiveLanguage] = React.useState("en")
  const [changeNote, setChangeNote] = React.useState("")
  const [historyOpen, setHistoryOpen] = React.useState(false)
  const [variablesOpen, setVariablesOpen] = React.useState(false)
  const [testSendOpen, setTestSendOpen] = React.useState(false)
  const [deleteOpen, setDeleteOpen] = React.useState(false)
  const [discardOpen, setDiscardOpen] = React.useState(false)

  React.useEffect(() => {
    if (!template) return
    const source = template.draftVersion ?? template.publishedVersion
    const loaded = toTranslationDrafts(source?.translations)
    setDrafts(loaded)
    setBaseline(loaded)
    setChangeNote(template.draftVersion?.changeNote ?? "")
    setActiveLanguage((current) =>
      loaded[current] || current === (template.defaultLanguage ?? "en")
        ? current
        : (template.defaultLanguage ?? "en")
    )
  }, [template])

  const active: TranslationDraft = drafts[activeLanguage] ?? EMPTY_DRAFT
  const variables = parseVariables(template?.typeVariablesJson)
  const rendererGlobals = React.useMemo(() => getRendererGlobals(t), [t])

  const isDirty =
    JSON.stringify(drafts) !== JSON.stringify(baseline) ||
    changeNote !== (template?.draftVersion?.changeNote ?? "")

  const updateActive = (patch: Partial<TranslationDraft>) =>
    setDrafts((current) => ({
      ...current,
      [activeLanguage]: { ...(current[activeLanguage] ?? EMPTY_DRAFT), ...patch },
    }))

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["notification-template", id] })
    void queryClient.invalidateQueries({ queryKey: ["notification-templates"] })
  }

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/notification-templates/{id}/draft", {
          params: { path: { id } },
          body: {
            translations: Object.entries(drafts)
              .filter(([, draft]) => draft.subject.trim() || draft.bodyHtml.trim())
              .map(([languageCode, draft]) => ({
                languageCode,
                subject: draft.subject,
                bodyHtml: draft.bodyHtml,
                bodyText: draft.bodyText || null,
              })),
            removeLanguages: Object.entries(baseline)
              .filter(
                ([language, previous]) =>
                  (previous.subject || previous.bodyHtml) &&
                  !(drafts[language]?.subject.trim() || drafts[language]?.bodyHtml.trim())
              )
              .map(([language]) => language),
            changeNote: changeNote || null,
            expectedModifiedAt: template?.modifiedAt ?? null,
          },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.draftSaved"))
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const publishMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/publish", {
          params: { path: { id } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.publishedToast"))
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const unpublishMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/notification-templates/{id}/unpublish", {
          params: { path: { id } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.unpublishedToast"))
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
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
      void queryClient.invalidateQueries({ queryKey: ["notification-templates"] })
      navigate("/notifications/templates")
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  if (query.isLoading || !template) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-[540px] w-full" />
      </div>
    )
  }

  const isSystemGlobal = Boolean(template.typeIsSystem && !template.applicationId)

  return (
    <div className="flex flex-col gap-6">
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
          <div className="flex flex-wrap items-center gap-2">
            {canManage ? (
              <Button
                variant="outline"
                disabled={!isDirty || saveMutation.isPending}
                onClick={() => saveMutation.mutate()}
              >
                {t("notifications.saveDraft")}
              </Button>
            ) : null}
            {canPublish ? (
              <Button
                disabled={!template.draftVersionId || isDirty || publishMutation.isPending}
                onClick={() => publishMutation.mutate()}
              >
                <Check data-icon="inline-start" />
                {t("notifications.publish")}
              </Button>
            ) : null}
            <Button variant="ghost" size="icon" onClick={() => setHistoryOpen(true)}>
              <History />
              <span className="sr-only">{t("notifications.versionHistory")}</span>
            </Button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" aria-label={t("common.actions")}>
                  <MoreHorizontal />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-56">
                <DropdownMenuGroup>
                  {canManage ? (
                    <DropdownMenuItem onClick={() => setTestSendOpen(true)}>
                      <Send />
                      {t("notifications.testSend")}
                    </DropdownMenuItem>
                  ) : null}
                  {canManage && template.draftVersionId ? (
                    <DropdownMenuItem onClick={() => setDiscardOpen(true)}>
                      {t("notifications.discardDraft")}
                    </DropdownMenuItem>
                  ) : null}
                  {canPublish && template.publishedVersionId && !isSystemGlobal ? (
                    <DropdownMenuItem
                      onClick={() => unpublishMutation.mutate()}
                    >
                      {t("notifications.unpublish")}
                    </DropdownMenuItem>
                  ) : null}
                </DropdownMenuGroup>
                {canManage && !isSystemGlobal ? (
                  <>
                    <DropdownMenuSeparator />
                    <DropdownMenuGroup>
                      <DropdownMenuItem
                        variant="destructive"
                        onClick={() => setDeleteOpen(true)}
                      >
                        {t("common.delete")}
                      </DropdownMenuItem>
                    </DropdownMenuGroup>
                  </>
                ) : null}
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
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

      <div className="grid gap-6 xl:grid-cols-2">
        <div className="flex flex-col gap-4">
          <Tabs value={activeLanguage} onValueChange={setActiveLanguage}>
            {/* Wrapping needs the height to follow the rows; the strip's
                default fixed height would cut off every row but the first. */}
            <TabsList className="h-auto! flex-wrap">
              {SUPPORTED_LANGUAGES.map((language) => {
                const draft = drafts[language.code]
                const filled = Boolean(draft?.subject.trim() || draft?.bodyHtml.trim())
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
                dir="auto"
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
              />
            </Field>

            <div className="flex flex-col gap-4">
              <VariablePalette
                variables={variables}
                onInsert={(placeholder) => {
                  if (!insertAtCursor(editorRef, placeholder)) {
                    updateActive({ bodyHtml: active.bodyHtml + placeholder })
                  }
                }}
              />
              <VariablePalette
                title={t("notifications.globalVariables")}
                variables={rendererGlobals}
                onInsert={(placeholder) => {
                  if (!insertAtCursor(editorRef, placeholder)) {
                    updateActive({ bodyHtml: active.bodyHtml + placeholder })
                  }
                }}
              />
              {canManage ? (
                <Button variant="ghost" size="sm" onClick={() => setVariablesOpen(true)}>
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
                dir="auto"
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
              <Input
                id="template-change-note"
                dir="auto"
                value={changeNote}
                onChange={(e) => setChangeNote(e.target.value)}
                placeholder={t("notifications.changeNotePlaceholder")}
                disabled={!canManage}
              />
            </Field>
          </FieldGroup>
        </div>

        <TemplatePreview
          notificationTypeId={template.notificationTypeId!}
          applicationId={template.applicationId}
          languageCode={activeLanguage}
          subject={active.subject}
          bodyHtml={active.bodyHtml}
          bodyText={active.bodyText}
        />
      </div>

      <VersionHistorySheet
        open={historyOpen}
        onOpenChange={setHistoryOpen}
        template={template}
        canPublish={canPublish}
        canManage={canManage}
      />

      <ManageVariablesDialog
        open={variablesOpen}
        onOpenChange={setVariablesOpen}
        template={template}
      />

      <TestSendDialog
        open={testSendOpen}
        onOpenChange={setTestSendOpen}
        templateId={id}
        defaultLanguage={template.defaultLanguage ?? "en"}
      />

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
    </div>
  )
}
