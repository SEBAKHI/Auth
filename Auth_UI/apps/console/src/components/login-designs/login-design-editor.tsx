import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror"
import { ArrowLeft, Check, Plus, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { getErrorMessage, getErrorStatus } from "@authsystem/api/errors"
import { directionForLanguage, SUPPORTED_LANGUAGES } from "@authsystem/i18n"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldTitle,
} from "@authsystem/ui/field"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"
import { Input } from "@authsystem/ui/input"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Switch } from "@authsystem/ui/switch"
import { Tabs, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import type { ThemeConfig } from "@authsystem/ui/theme/theme-config"

import { CodeEditor } from "@/pages/notifications/components/code-editor"
import { insertAtCursor } from "@/pages/notifications/components/code-editor-utils"
import { VariablePalette } from "@/pages/notifications/components/variable-palette"
import type { TemplateVariable } from "@/pages/notifications/lib"

import { DesignPreview } from "./design-preview"
import {
  designKey,
  getDesign,
  previewDesign,
  saveDesign,
  type DesignScope,
  type LoginDesign,
} from "./login-design-api"

/** Per-language texts: `{ "<lang>": { "<key>": "…" } }`. */
type DesignStrings = Record<string, Record<string, string>>

function parseStrings(json: string): DesignStrings {
  try {
    const parsed: unknown = JSON.parse(json)
    return typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)
      ? (parsed as DesignStrings)
      : {}
  } catch {
    return {}
  }
}

/**
 * One text of the design: its key and its value in each language. The row's id holds the
 * texts, never the key: a key being typed passes through other keys' names, and a duplicate
 * left in place still keeps both rows' texts.
 */
type TextRow = { id: number; key: string; values: Record<string, string> }

/** The rows of a stored document, one per key any language has, in the order first met. */
function rowsOf(strings: DesignStrings): TextRow[] {
  const rows: TextRow[] = []
  for (const [language, texts] of Object.entries(strings)) {
    for (const [key, value] of Object.entries(texts ?? {})) {
      let row = rows.find((candidate) => candidate.key === key)
      if (!row) {
        row = { id: rows.length, key, values: {} }
        rows.push(row)
      }
      row.values[language] = value
    }
  }
  return rows
}

/** A key a design can print as `strings.<key>`: a Liquid identifier. */
const STRING_KEY = /^[A-Za-z_][A-Za-z0-9_]*$/

/**
 * The document the API takes, built from the rows when a preview or a save sends it. A row
 * whose key is not an identifier yet, or repeats an earlier row's, is left out until it is
 * fixed (Save waits for that).
 */
function stringsOf(rows: TextRow[]): DesignStrings {
  const strings: DesignStrings = {}
  const used = new Set<string>()
  for (const row of rows) {
    if (!STRING_KEY.test(row.key) || used.has(row.key)) continue
    used.add(row.key)
    for (const [language, value] of Object.entries(row.values)) {
      const texts = (strings[language] ??= {})
      texts[row.key] = value
    }
  }
  return strings
}

/** A document compared by what it says, not by the order its keys come in. */
function canonical(strings: DesignStrings): string {
  return JSON.stringify(
    Object.keys(strings)
      .filter((language) => Object.keys(strings[language] ?? {}).length > 0)
      .sort()
      .map((language) => [language, Object.entries(strings[language] ?? {}).sort(([a], [b]) => (a < b ? -1 : 1))])
  )
}

function DesignUnavailable({ message, onClose }: { message: string; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-4">
      <Button variant="ghost" className="w-fit" onClick={onClose}>
        <ArrowLeft data-icon="inline-start" className="rtl:rotate-180" />
        {t("loginDesigns.back")}
      </Button>
      <Alert variant="destructive">
        <AlertDescription>{message}</AlertDescription>
      </Alert>
    </div>
  )
}

/**
 * One design: its name, whether the platform's mark keeps the corner, its HTML and CSS,
 * its texts per language, and a preview through the same frame the sign-in pages use.
 * There is no draft: on the active design, Save is what visitors see next.
 */
export function LoginDesignEditor({
  scope,
  designId,
  canManage,
  appearance,
  onClose,
  onSaved,
}: {
  scope: DesignScope
  designId: string
  canManage: boolean
  appearance: ThemeConfig | null
  onClose: () => void
  onSaved: () => void
}) {
  const { t } = useTranslation()
  const query = useQuery({
    queryKey: designKey(scope, designId),
    queryFn: () => getDesign(scope, designId),
  })

  if (!query.data) {
    // A design deleted meanwhile (from another tab, by another administrator) or a stale
    // link: say so and offer the way back, rather than a skeleton that never resolves.
    if (query.isError) {
      return (
        <DesignUnavailable
          message={
            getErrorStatus(query.error) === 404
              ? t("loginDesigns.notFound")
              : getErrorMessage(query.error, t("loginDesigns.loadFailed"))
          }
          onClose={onClose}
        />
      )
    }
    return <Skeleton className="h-[640px] w-full" />
  }

  // Seeded once from the design as loaded: a refetch never resets what is being edited.
  return (
    <EditorForm
      scope={scope}
      design={query.data}
      canManage={canManage}
      appearance={appearance}
      onClose={onClose}
      onSaved={onSaved}
    />
  )
}

function EditorForm({
  scope,
  design,
  canManage,
  appearance,
  onClose,
  onSaved,
}: {
  scope: DesignScope
  design: LoginDesign
  canManage: boolean
  appearance: ThemeConfig | null
  onClose: () => void
  onSaved: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const editorRef = React.useRef<ReactCodeMirrorRef>(null)

  const [saved, setSaved] = React.useState(design)
  const [name, setName] = React.useState(design.name)
  const [showPlatformLogo, setShowPlatformLogo] = React.useState(design.showPlatformLogo)
  const [content, setContent] = React.useState(design.content)
  const [rows, setRows] = React.useState(() => rowsOf(parseStrings(design.stringsJson)))
  const nextRowId = React.useRef(rows.length)
  const [language, setLanguage] = React.useState("en")

  const keys = rows.map((row) => row.key)
  const strings = React.useMemo(() => stringsOf(rows), [rows])
  const stringsJson = React.useMemo(() => JSON.stringify(strings), [strings])
  const isDirty =
    name !== saved.name ||
    showPlatformLogo !== saved.showPlatformLogo ||
    content !== saved.content ||
    canonical(strings) !== canonical(parseStrings(saved.stringsJson))
  const keysValid = keys.every((key) => STRING_KEY.test(key)) && new Set(keys).size === keys.length

  const debounced = useDebouncedValue(
    React.useMemo(() => ({ content, stringsJson, language }), [content, stringsJson, language]),
    600
  )
  const preview = useQuery({
    queryKey: [...designKey(scope, design.id), "preview", debounced],
    queryFn: () => previewDesign(scope, debounced),
    enabled: debounced.content.trim() !== "",
    placeholderData: (previous) => previous,
    retry: false,
  })

  const save = useMutation({
    mutationFn: () =>
      saveDesign(scope, design.id, { name: name.trim(), content, stringsJson, showPlatformLogo }, saved.revision),
    onSuccess: (next) => {
      setSaved(next)
      queryClient.setQueryData(designKey(scope, design.id), next)
      toast.success(next.isActive ? t("loginDesigns.savedLive") : t("loginDesigns.saved"))
      onSaved()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const unsavedPrompt = useUnsavedChangesPrompt({ isDirty, isSaving: save.isPending })

  const variables: TemplateVariable[] = React.useMemo(
    () => [
      { name: "App.Name", description: t("loginDesigns.varAppName") },
      { name: "Platform.Name", description: t("loginDesigns.varPlatformName") },
      { name: "lang", description: t("loginDesigns.varLang") },
      { name: "dir", description: t("loginDesigns.varDir") },
      ...keys
        .filter((key) => STRING_KEY.test(key))
        .map((key) => ({
          name: `strings.${key}`,
          insertText: `{{ strings.${key} | raw }}`,
          description: t("loginDesigns.varString"),
        })),
    ],
    [keys, t]
  )

  const insert = (snippet: string) => {
    if (!insertAtCursor(editorRef, snippet)) setContent((current) => current + snippet)
  }

  const updateRow = (id: number, change: (row: TextRow) => TextRow) =>
    setRows((current) => current.map((row) => (row.id === id ? change(row) : row)))

  const setText = (id: number, value: string) =>
    updateRow(id, (row) => ({ ...row, values: { ...row.values, [language]: value } }))

  const renameKey = (id: number, key: string) => updateRow(id, (row) => ({ ...row, key }))

  const removeKey = (id: number) => setRows((current) => current.filter((row) => row.id !== id))

  const addKey = () => {
    let index = keys.length + 1
    while (keys.includes(`text${index}`)) index++
    const id = nextRowId.current++
    setRows((current) => [...current, { id, key: `text${index}`, values: {} }])
  }

  const contentDir = directionForLanguage(language)
  // Enums travel by name (JsonStringEnumConverter) whatever the schema types them as.
  const warnings = (preview.data?.warnings ?? []).map(String)

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-2">
        {/* Leaving with unsaved changes is the page's unsaved-changes prompt to stop. */}
        <Button variant="ghost" onClick={onClose}>
          <ArrowLeft data-icon="inline-start" className="rtl:rotate-180" />
          {t("loginDesigns.back")}
        </Button>
        <div className="flex flex-wrap items-center gap-2">
          {saved.isActive ? <Badge>{t("loginDesigns.active")}</Badge> : null}
          {isDirty ? <Badge variant="outline">{t("loginDesigns.unsaved")}</Badge> : null}
          {canManage ? (
            <Button
              disabled={!isDirty || !keysValid || name.trim() === "" || save.isPending}
              onClick={() => save.mutate()}
            >
              <Check data-icon="inline-start" />
              {t("loginDesigns.save")}
            </Button>
          ) : null}
        </div>
      </div>

      {saved.isActive && canManage ? (
        <Alert>
          <AlertDescription>{t("loginDesigns.liveNotice")}</AlertDescription>
        </Alert>
      ) : null}

      <div className="flex flex-col gap-6 xl:flex-row">
        <div className="flex min-w-0 flex-col gap-4 xl:flex-1">
          <FieldGroup>
            <Field data-disabled={!canManage}>
              <FieldLabel htmlFor="login-design-name">{t("loginDesigns.name")}</FieldLabel>
              <Input
                id="login-design-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                disabled={!canManage}
                aria-invalid={name.trim() === ""}
              />
            </Field>

            <Field orientation="horizontal" data-disabled={!canManage}>
              <FieldContent>
                <FieldLabel htmlFor="login-design-platform-logo">{t("loginDesigns.showPlatformLogo")}</FieldLabel>
                <FieldDescription>{t("loginDesigns.showPlatformLogoHint")}</FieldDescription>
              </FieldContent>
              <Switch
                id="login-design-platform-logo"
                checked={showPlatformLogo}
                onCheckedChange={setShowPlatformLogo}
                disabled={!canManage}
              />
            </Field>

            {/* The editor is a CodeMirror surface, not a labelable control: the field is
                titled and the editor carries the same name. */}
            <Field>
              <FieldTitle>{t("loginDesigns.content")}</FieldTitle>
              <FieldDescription>{t("loginDesigns.contentHint")}</FieldDescription>
              <CodeEditor
                ref={editorRef}
                value={content}
                onChange={setContent}
                ariaLabel={t("loginDesigns.content")}
                allowImages
                imagePurpose="Panel"
                readOnly={!canManage}
              />
            </Field>

            <VariablePalette
              title={t("loginDesigns.variables")}
              variables={variables}
              onInsert={canManage ? insert : undefined}
            />
          </FieldGroup>

          <Tabs value={language} onValueChange={setLanguage}>
            {/* Wrapping needs the height to follow the rows. */}
            <TabsList className="h-auto! flex-wrap">
              {SUPPORTED_LANGUAGES.map((option) => (
                <TabsTrigger key={option.code} value={option.code}>
                  <span className="uppercase">{option.code}</span>
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>

          <FieldGroup>
            <FieldTitle>{t("loginDesigns.texts", { language: language.toUpperCase() })}</FieldTitle>
            <FieldDescription>
              {t("loginDesigns.textsHint", { example: "{{ strings.headline | raw }}" })}
            </FieldDescription>
            {rows.map(({ id, key, values }, index) => {
              const invalid = !STRING_KEY.test(key) || keys.indexOf(key) !== index
              return (
                <Field key={id} orientation="horizontal" data-invalid={invalid || undefined}>
                  <Input
                    aria-label={t("loginDesigns.textKey")}
                    className="w-40 shrink-0 font-mono"
                    value={key}
                    onChange={(event) => renameKey(id, event.target.value.trim())}
                    aria-invalid={invalid}
                    disabled={!canManage}
                    spellCheck={false}
                  />
                  <Input
                    aria-label={t("loginDesigns.textValue", { key })}
                    dir={contentDir}
                    value={values[language] ?? ""}
                    onChange={(event) => setText(id, event.target.value)}
                    disabled={!canManage}
                  />
                  {canManage ? (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={t("loginDesigns.removeText", { key })}
                      onClick={() => removeKey(id)}
                    >
                      <Trash2 />
                    </Button>
                  ) : null}
                </Field>
              )
            })}
            {canManage ? (
              <div>
                <Button variant="outline" size="sm" onClick={addKey}>
                  <Plus data-icon="inline-start" />
                  {t("loginDesigns.addText")}
                </Button>
              </div>
            ) : null}
          </FieldGroup>
        </div>

        <div className="flex min-w-0 flex-col gap-3 xl:flex-1">
          {warnings.length > 0 ? (
            <Alert>
              <AlertTitle>{t("loginDesigns.warningsTitle")}</AlertTitle>
              <AlertDescription>
                <ul className="list-inside list-disc">
                  {warnings.includes("Script") || warnings.includes("1") ? <li>{t("loginDesigns.warningScript")}</li> : null}
                  {warnings.includes("EventHandler") || warnings.includes("2") ? <li>{t("loginDesigns.warningEventHandler")}</li> : null}
                </ul>
              </AlertDescription>
            </Alert>
          ) : null}
          <DesignPreview
            html={preview.data?.html ?? null}
            lang={preview.data?.language ?? language}
            dir={preview.data?.dir === "rtl" ? "rtl" : "ltr"}
            appearance={appearance}
            error={preview.isError ? getErrorMessage(preview.error, t("loginDesigns.previewFailed")) : null}
          />
        </div>
      </div>

      {unsavedPrompt}
    </div>
  )
}
