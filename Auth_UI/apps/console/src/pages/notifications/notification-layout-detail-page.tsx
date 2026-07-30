import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Check } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { PageHeader } from "@astoom/ui/common/page-header"
import { usePageBreadcrumb } from "@astoom/ui/crumbs"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldTitle,
} from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { useDebouncedValue } from "@astoom/ui/hooks/use-debounced-value"
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror"
import { PERMISSIONS } from "@/lib/constants"
import { CodeEditor, insertAtCursor } from "./components/code-editor"
import { PreviewPane } from "./components/preview-pane"
import { VariablePalette } from "./components/variable-palette"
import { getRendererGlobals, type NotificationPreviewDto, type TemplateVariable } from "./lib"

/** Per-language chrome strings stored in the layout's StringsJson. */
type LayoutStrings = Record<string, Record<string, string>>

function parseStrings(json: string | null | undefined): LayoutStrings {
  if (!json) return {}
  try {
    const parsed: unknown = JSON.parse(json)
    return typeof parsed === "object" && parsed !== null ? (parsed as LayoutStrings) : {}
  } catch {
    return {}
  }
}

/**
 * Layout editor: the Liquid HTML document with its content slot, per-language
 * footer strings, and a live preview with placeholder body content. Publishing
 * copies the draft to the live columns atomically.
 */
export function NotificationLayoutDetailPage() {
  const { id = "" } = useParams()
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const editorRef = React.useRef<ReactCodeMirrorRef>(null)

  const canManage = hasPermission(PERMISSIONS.notificationLayouts.manage)

  // The layout's placeholder slots are a fixed contract with the renderer —
  // it injects exactly these into every message. Unlike the type variable
  // catalog there is nothing to manage: a custom slot would render empty on
  // every send, and removing the content slot would blank every message.
  const layoutSlots: TemplateVariable[] = React.useMemo(
    () => [
      {
        name: "content",
        insertText: "{{ content | raw }}",
        required: true,
        description: t("notifications.layoutSlotContent"),
      },
      {
        name: "dir",
        insertText: "{{ dir }}",
        description: t("notifications.layoutSlotDir"),
      },
      {
        name: "lang",
        insertText: "{{ lang }}",
        description: t("notifications.layoutSlotLang"),
      },
      {
        name: "strings.footer",
        insertText: "{{ strings.footer | raw }}",
        description: t("notifications.layoutSlotFooter"),
      },
    ],
    [t]
  )

  // Renderer globals (Platform/Application/SenderName/Year) are available to
  // layouts exactly as they are to templates — shown so nothing stays guesswork.
  const rendererGlobals = React.useMemo(() => getRendererGlobals(t), [t])

  const query = useQuery({
    queryKey: ["notification-layout", id],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/notification-layouts/{id}", {
          params: { path: { id } },
        })
      ),
    enabled: Boolean(id),
  })
  const layout = query.data

  usePageBreadcrumb(layout ? layout.name : undefined)

  const [name, setName] = React.useState("")
  const [content, setContent] = React.useState("")
  const [strings, setStrings] = React.useState<LayoutStrings>({})
  const [previewLanguage, setPreviewLanguage] = React.useState("en")
  const [preview, setPreview] = React.useState<NotificationPreviewDto | null>(null)

  React.useEffect(() => {
    if (!layout) return
    setName(layout.name ?? "")
    setContent(layout.draftContent ?? "")
    setStrings(parseStrings(layout.draftStringsJson))
  }, [layout])

  const stringsJson = React.useMemo(() => JSON.stringify(strings), [strings])
  const isDirty =
    Boolean(layout) &&
    (name !== (layout?.name ?? "") ||
      content !== (layout?.draftContent ?? "") ||
      stringsJson !== JSON.stringify(parseStrings(layout?.draftStringsJson)))

  const debouncedPreviewInput = useDebouncedValue(
    React.useMemo(
      () => ({ content, stringsJson, previewLanguage }),
      [content, stringsJson, previewLanguage]
    ),
    600
  )

  const previewMutation = useMutation({
    mutationFn: (input: { content: string; stringsJson: string; previewLanguage: string }) =>
      unwrap(
        api.POST("/api/v1/notification-layouts/preview", {
          body: {
            layoutContent: input.content,
            layoutStringsJson: input.stringsJson,
            languageCode: input.previewLanguage,
          },
        })
      ),
    onSuccess: (data) => setPreview(data),
  })

  const renderPreview = previewMutation.mutate
  React.useEffect(() => {
    if (debouncedPreviewInput.content) {
      renderPreview(debouncedPreviewInput)
    }
  }, [debouncedPreviewInput, renderPreview])

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["notification-layout", id] })
    void queryClient.invalidateQueries({ queryKey: ["notification-layouts"] })
  }

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/notification-layouts/{id}/draft", {
          params: { path: { id } },
          body: {
            name,
            draftContent: content,
            draftStringsJson: stringsJson,
            expectedModifiedAt: layout?.modifiedAt ?? null,
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
        api.POST("/api/v1/notification-layouts/{id}/publish", {
          params: { path: { id } },
        })
      ),
    onSuccess: () => {
      toast.success(t("notifications.publishedToast"))
      invalidate()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  if (query.isLoading || !layout) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-[540px] w-full" />
      </div>
    )
  }

  const activeStrings = strings[previewLanguage] ?? {}

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={layout.name ?? ""}
        description={
          layout.applicationName
            ? t("notifications.appScopeDescription", {
                application: layout.applicationName,
              })
            : t("notifications.globalScopeDescription")
        }
        actions={
          canManage ? (
            <div className="flex flex-wrap items-center gap-2">
              <Button
                variant="outline"
                disabled={!isDirty || saveMutation.isPending}
                onClick={() => saveMutation.mutate()}
              >
                {t("notifications.saveDraft")}
              </Button>
              <Button
                disabled={isDirty || !layout.hasUnpublishedChanges || publishMutation.isPending}
                onClick={() => publishMutation.mutate()}
              >
                <Check data-icon="inline-start" />
                {t("notifications.publish")}
              </Button>
            </div>
          ) : null
        }
      />

      <div className="flex flex-wrap items-center gap-2">
        {layout.isPublished ? (
          <Badge>{t("notifications.published")}</Badge>
        ) : (
          <Badge variant="outline">{t("notifications.unpublished")}</Badge>
        )}
        {layout.hasUnpublishedChanges ? (
          <Badge variant="secondary">{t("notifications.unpublishedChanges")}</Badge>
        ) : null}
        {isDirty ? (
          <Badge variant="outline">{t("notifications.unsavedChanges")}</Badge>
        ) : null}
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <div className="flex flex-col gap-4">
          <Tabs value={previewLanguage} onValueChange={setPreviewLanguage}>
            {/* Wrapping needs the height to follow the rows; the strip's
                default fixed height would cut off every row but the first. */}
            <TabsList className="h-auto! flex-wrap">
              {SUPPORTED_LANGUAGES.map((language) => {
                const filled = Boolean(strings[language.code]?.footer?.trim())
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
              <FieldLabel htmlFor="layout-name">
                {t("notifications.layoutName")}
              </FieldLabel>
              <Input
                id="layout-name"
                dir="auto"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("notifications.layoutNamePlaceholder")}
                disabled={!canManage}
              />
            </Field>

            {/* The editor is a CodeMirror surface, not a labelable control, so
                the field is titled and the editor carries the same aria-label. */}
            <Field>
              <FieldTitle>{t("notifications.layoutContent")}</FieldTitle>
              <CodeEditor
                ref={editorRef}
                value={content}
                onChange={setContent}
                minHeight="380px"
                ariaLabel={t("notifications.layoutContent")}
                allowImages
              />
            </Field>

            <VariablePalette
              title={t("notifications.layoutSlots")}
              variables={layoutSlots}
              onInsert={(placeholder) => {
                if (!insertAtCursor(editorRef, placeholder)) {
                  setContent((current) => current + placeholder)
                }
              }}
            />

            <VariablePalette
              title={t("notifications.globalVariables")}
              variables={rendererGlobals}
              onInsert={(placeholder) => {
                if (!insertAtCursor(editorRef, placeholder)) {
                  setContent((current) => current + placeholder)
                }
              }}
            />

            <Field data-disabled={!canManage}>
              <FieldLabel htmlFor="layout-footer">
                {t("notifications.layoutFooter", { language: previewLanguage.toUpperCase() })}
              </FieldLabel>
              <Input
                id="layout-footer"
                dir="auto"
                value={activeStrings.footer ?? ""}
                onChange={(e) =>
                  setStrings((current) => ({
                    ...current,
                    [previewLanguage]: {
                      ...(current[previewLanguage] ?? {}),
                      footer: e.target.value,
                    },
                  }))
                }
                placeholder={t("notifications.layoutFooterPlaceholder")}
                disabled={!canManage}
              />
              <FieldDescription>
                {t("notifications.layoutFooterHint")}
              </FieldDescription>
            </Field>
          </FieldGroup>
        </div>

        <div className="flex flex-col gap-3">
          <p className="text-sm font-medium">{t("notifications.preview")}</p>
          <PreviewPane
            preview={preview}
            frameHeight="560px"
            error={
              previewMutation.isError
                ? previewMutation.error instanceof Error
                  ? previewMutation.error.message
                  : t("notifications.previewFailed")
                : null
            }
          />
        </div>
      </div>
    </div>
  )
}
