import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Check, CheckCircle2, Plus, Save, Trash2, TriangleAlert } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { directionForLanguage, SUPPORTED_LANGUAGES } from "@authsystem/i18n"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@authsystem/ui/card"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { PageHeader } from "@authsystem/ui/common/page-header"
import {
  type PolicyDisclosure,
  type PrivacyPolicyContent,
} from "@authsystem/ui/common/policy-document"
import { usePageBreadcrumb } from "@authsystem/ui/crumbs"
import { DatePicker, monthsFromNow } from "@authsystem/ui/common/date-picker"
import { Field, FieldDescription, FieldGroup, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Skeleton } from "@authsystem/ui/skeleton"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"
import { Tabs, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { Textarea } from "@authsystem/ui/textarea"
import { PERMISSIONS } from "@/lib/constants"
import {
  SectionListEditor,
  StringListEditor,
} from "./components/policy-field-editors"
import { PolicyLanguageGapNotice } from "./components/policy-language-gap-notice"
import { PolicyPreviewPane } from "./components/policy-preview-pane"
import { PolicyVersionField } from "./components/policy-version-field"
import { Spinner } from "@authsystem/ui/spinner"
import {
  PolicyTokenPalette,
  useFocusedField,
} from "./components/policy-token-palette"

/** Shape used when starting a language that has no document yet. */
const EMPTY_DOCUMENT: PrivacyPolicyContent = {
  title: "",
  effectiveDate: "",
  versionLabel: "Version",
  intro: [""],
  sections: [],
  retention: {
    heading: "",
    intro: "",
    columns: ["", "", ""],
    rows: [],
  },
  deletion: {
    heading: "",
    paragraphs: [""],
    bullets: [""],
    button: "",
    signedInHint: "",
  },
  rights: [],
  closing: [],
  contactDpoLabel: "",
  contactVerbisLabel: "",
  contactKepLabel: "",
  unfilledWarning: "",
}

/**
 * Structured editor for one policy revision, with a live preview rendered by
 * the SAME component the public page uses — so what an editor sees is what a
 * user will get.
 *
 * Numbers are never typed in: the {{token}} palette is substituted from the
 * running AccountDeletionSettings at render time, which is what keeps the
 * published text from drifting when configuration changes.
 */
export function NotificationPolicyDetailPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  // Routed by the revision's immutable id, never by its version string:
  // the string is editable, and a URL keyed on editable data breaks the
  // moment it is edited (the page 404s and in-flight edits are stranded).
  const { id = "" } = useParams()

  const canManage = hasPermission(PERMISSIONS.privacyPolicy.manage)

  const [language, setLanguage] = React.useState("en")
  const [doc, setDoc] = React.useState<PrivacyPolicyContent | null>(null)
  const [dirty, setDirty] = React.useState(false)
  const [parseError, setParseError] = React.useState<string | null>(null)
  const [pendingLanguage, setPendingLanguage] = React.useState<string | null>(null)
  const [confirmingPublish, setConfirmingPublish] = React.useState(false)
  const [versionName, setVersionName] = React.useState("")
  const [effectiveDate, setEffectiveDate] = React.useState("")
  const [changeNote, setChangeNote] = React.useState("")
  const [metaDirty, setMetaDirty] = React.useState(false)

  const { onFocusCapture, insert } = useFocusedField()

  const versionsQuery = useQuery({
    queryKey: ["privacy-policy-versions"],
    queryFn: () => unwrap(api.GET("/api/v1/privacy-policy/versions")),
  })
  const versionRow = versionsQuery.data?.find((v) => v.id === id)
  // The API keys content by version string, so it is resolved from the row
  // on every render — a rename is picked up without touching the URL.
  const version = versionRow?.version ?? ""

  usePageBreadcrumb(version || undefined)

  React.useEffect(() => {
    if (!versionRow || metaDirty) return
    setVersionName(versionRow.version ?? "")
    setEffectiveDate((versionRow.effectiveDateUtc ?? "").slice(0, 10))
    setChangeNote(versionRow.changeNote ?? "")
  }, [versionRow, metaDirty])

  const contentQuery = useQuery({
    queryKey: ["privacy-policy-content", id, language],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/privacy-policy/versions/content", {
          params: { query: { version, language } },
        })
      ),
    enabled: Boolean(version),
  })

  // The live disclosure drives the preview's numbers, exactly as on the
  // public page — so the preview shows real values, never raw tokens.
  const publishedQuery = useQuery({
    queryKey: ["privacy-policy-disclosure"],
    queryFn: () =>
      unwrap(api.GET("/api/v1/privacy-policy/published", { params: { query: {} } })),
    staleTime: 5 * 60 * 1000,
  })

  React.useEffect(() => {
    if (!contentQuery.data || dirty) return
    const raw = contentQuery.data.contentJson
    if (!raw) {
      setDoc(structuredClone(EMPTY_DOCUMENT))
      setParseError(null)
      return
    }
    try {
      setDoc(JSON.parse(raw) as PrivacyPolicyContent)
      setParseError(null)
    } catch (error) {
      setDoc(null)
      setParseError((error as Error).message)
    }
  }, [contentQuery.data, dirty])

  /**
   * One Save for the whole page. Splitting it into "save content" and "save
   * version details" made the header button look broken while the details
   * were being edited, and let a rename land while document edits were still
   * unsaved — stranding them under a key that no longer existed.
   *
   * Order matters: the document is written FIRST, under the version string
   * still in force, and only then may the rename take effect. A failure
   * therefore aborts before the identifier moves, never after.
   */
  const saveMutation = useMutation({
    mutationFn: async () => {
      if (dirty && doc) {
        await unwrap(
          api.PUT("/api/v1/privacy-policy/versions/content", {
            body: {
              version,
              languageCode: language,
              contentJson: JSON.stringify(doc),
            },
          })
        )
      }
      if (metaDirty) {
        await unwrap(
          api.PUT("/api/v1/privacy-policy/versions", {
            body: {
              version,
              newVersion: versionName.trim() || null,
              effectiveDateUtc: effectiveDate + "T00:00:00Z",
              changeNote: changeNote.trim() || null,
            },
          })
        )
      }
    },
    onSuccess: () => {
      setDirty(false)
      setMetaDirty(false)
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-content"] })
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      toast.success(t("notifications.policyContentSaved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const publishMutation = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/privacy-policy/versions/publish", { body: { version } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      toast.success(t("notifications.policyPublishedToast"))
      setConfirmingPublish(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  // Leaving with unsaved legal text must never be silent.
  const unsavedPrompt = useUnsavedChangesPrompt(dirty || metaDirty)

  const patch = React.useCallback((next: Partial<PrivacyPolicyContent>) => {
    setDoc((current) => (current ? { ...current, ...next } : current))
    setDirty(true)
  }, [])

  const disclosure: PolicyDisclosure = {
    graceDays: Number(publishedQuery.data?.disclosure?.graceDays ?? 30),
    otpValidityMinutes: Number(publishedQuery.data?.disclosure?.otpValidityMinutes ?? 15),
    loginAttemptRetentionDays: Number(
      publishedQuery.data?.disclosure?.loginAttemptRetentionDays ?? 365
    ),
    outboxRetentionDays: Number(
      publishedQuery.data?.disclosure?.outboxRetentionDays ?? 180
    ),
    identifierReservationDays: Number(
      publishedQuery.data?.disclosure?.identifierReservationDays ?? 1095
    ),
    policyVersion: publishedQuery.data?.disclosure?.policyVersion ?? version,
    // Controller identity is edited in System Settings -> Data controller, not
    // here. The preview renders the live values so an unfilled controller shows
    // its draft banner in the editor, which is where it can still be fixed.
    legalName: publishedQuery.data?.disclosure?.legalName ?? "",
    address: publishedQuery.data?.disclosure?.address ?? "",
    privacyEmail: publishedQuery.data?.disclosure?.privacyEmail ?? "",
    emailProvider: publishedQuery.data?.disclosure?.emailProvider ?? "",
    hostingProvider: publishedQuery.data?.disclosure?.hostingProvider ?? "",
    hostingCountry: publishedQuery.data?.disclosure?.hostingCountry ?? "",
    dpoContact: publishedQuery.data?.disclosure?.dpoContact ?? "",
    verbisNo: publishedQuery.data?.disclosure?.verbisNo ?? "",
    kepAddress: publishedQuery.data?.disclosure?.kepAddress ?? "",
  }

  // The retention table's cells are rendered from index/key loops, so their
  // placeholders are keyed the same way — each column and each cell of a row
  // shows an example of the text that belongs in it.
  const retentionColumnPlaceholders = [
    t("notifications.policyColumnCategoryPlaceholder"),
    t("notifications.policyColumnPeriodPlaceholder"),
    t("notifications.policyColumnOutcomePlaceholder"),
  ]
  const retentionRowPlaceholders = {
    category: t("notifications.policyRowCategoryPlaceholder"),
    retention: t("notifications.policyRowPeriodPlaceholder"),
    detail: t("notifications.policyRowDetailPlaceholder"),
  }

  // A published or already-announced revision's identifier is referenced by
  // deletion records and by users' inboxes, so it can no longer move.
  const versionLocked = Boolean(versionRow?.isPublished || versionRow?.notifiedAtUtc)

  // Direction of the locale being *edited*, which is not the console's. Every
  // control holding this document's copy takes it, rather than `dir="auto"`:
  // `auto` reads the value, so an empty field always computed `ltr` and a new
  // Arabic translation opened left-aligned with the caret on the wrong edge.
  const dir = directionForLanguage(language)

  // A stale bookmark or a deleted revision must say so, not spin forever.
  if (versionsQuery.isSuccess && !versionRow) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader
          title={t("notifications.tabPolicy")}
          description={t("notifications.policyContentDescription")}
        />
        <Alert variant="destructive">
          <AlertDescription>{t("notifications.policyNotFound")}</AlertDescription>
        </Alert>
      </div>
    )
  }

  return (
    // From `xl` the page fills the shell's height and the editor/preview pair
    // below scrolls per column, so the rendered policy stays in view while its
    // sections are being edited. Narrower than that the two stack and the page
    // scrolls once.
    <div className="flex flex-col gap-6 xl:min-h-0 xl:flex-1">
      {unsavedPrompt}
      <PageHeader
        title={t("notifications.policyContentTitle", { version })}
        description={t("notifications.policyContentDescription")}
        actions={
          canManage ? (
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                disabled={(!dirty && !metaDirty) || !doc || saveMutation.isPending}
                onClick={() => saveMutation.mutate()}
              >
                {saveMutation.isPending ? (
                  <Spinner />
                ) : (
                  <Save data-icon="inline-start" />
                )}
                {t("common.save")}
              </Button>
              {versionRow && !versionRow.isPublished ? (
                <Button
                  disabled={dirty || metaDirty || publishMutation.isPending}
                  onClick={() => setConfirmingPublish(true)}
                >
                  <CheckCircle2 data-icon="inline-start" />
                  {t("notifications.policyPublish")}
                </Button>
              ) : null}
            </div>
          ) : undefined
        }
      />

      {/* Reported, not repaired. Re-rendering a published notice because a
          setting moved would amend the text people were shown with no version
          and no effective date to mark it — so the operator is told, and
          re-publishing stays their decision. */}
      {versionRow?.disclosureOutOfDate ? (
        <Alert>
          <TriangleAlert />
          <AlertTitle>{t("notifications.policyDisclosureDriftTitle")}</AlertTitle>
          <AlertDescription>
            {t("notifications.policyDisclosureDriftBody")}
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        {versionRow?.isPublished ? (
          <Badge>
            <CheckCircle2 data-icon="inline-start" />
            {t("notifications.policyPublished")}
          </Badge>
        ) : (
          <Badge variant="outline">{t("notifications.policyDraft")}</Badge>
        )}
        {versionRow?.notifiedAtUtc ? (
          <Badge variant="secondary">
            {t("notifications.policyNotifiedBadge", {
              count: versionRow.notifiedCount ?? 0,
            })}
          </Badge>
        ) : (
          <Badge variant="outline">{t("notifications.policyNotNotified")}</Badge>
        )}
        {dirty || metaDirty ? (
          <Badge variant="outline">{t("notifications.unsavedChanges")}</Badge>
        ) : null}
      </div>

      <Tabs
        value={language}
        onValueChange={(next: string) => {
          // Switching language refetches the document, so unsaved edits in
          // the current one would be lost without asking.
          if (dirty) {
            setPendingLanguage(next)
            return
          }
          setLanguage(next)
        }}
      >
        {/* Wrapping needs the height to follow the rows; the default fixed
            height would cut off every row but the first. */}
        <TabsList className="h-auto! flex-wrap">
          {SUPPORTED_LANGUAGES.map((lang) => {
            const written = (versionRow?.languages ?? []).includes(lang.code)
            return (
              <TabsTrigger key={lang.code} value={lang.code} title={lang.label}>
                <span dir="ltr" className="uppercase">
                  {lang.code}
                </span>
                {written ? <Check className="size-3" /> : null}
              </TabsTrigger>
            )
          })}
        </TabsList>
      </Tabs>

      <PolicyTokenPalette onInsert={insert} disabled={!canManage} />

      {/* Publishing turns this text into the notice of record and renders the
          document every reader is then served, so it is confirmed rather than
          fired from a single click — and the confirmation names the languages
          that will fall back to English. */}
      <ConfirmDialog
        open={confirmingPublish}
        onOpenChange={setConfirmingPublish}
        title={t("notifications.policyPublishTitle")}
        description={t("notifications.policyPublishBody")}
        confirmLabel={t("notifications.policyPublish")}
        loading={publishMutation.isPending}
        onConfirm={() => publishMutation.mutate()}
      >
        <PolicyLanguageGapNotice languages={versionRow?.languages} />
      </ConfirmDialog>

      <ConfirmDialog
        open={pendingLanguage !== null}
        onOpenChange={(open) => {
          if (!open) setPendingLanguage(null)
        }}
        title={t("common.discardTitle")}
        description={t("common.discardBody")}
        confirmLabel={t("common.discard")}
        destructive
        onConfirm={() => {
          if (pendingLanguage) {
            setDirty(false)
            setLanguage(pendingLanguage)
          }
          setPendingLanguage(null)
        }}
      />

      {parseError ? (
        <Alert variant="destructive">
          <AlertDescription className="font-mono text-xs">
            {parseError}
          </AlertDescription>
        </Alert>
      ) : null}

      {contentQuery.isLoading || !doc ? (
        <Skeleton className="h-96 w-full" />
      ) : (
        // A flex row rather than a two-column grid: `flex-1` splits the width
        // exactly as `grid-cols-2` did, and only a flex child can be told to
        // shrink below its content and scroll.
        <div className="flex flex-col gap-6 xl:min-h-0 xl:flex-1 xl:flex-row">
          {/* Editor */}
          <div
            className="flex flex-col gap-6 xl:min-h-0 xl:min-w-0 xl:flex-1 xl:overflow-y-auto"
            onFocusCapture={onFocusCapture}
          >
            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyVersionDetails")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="meta-version">
                      {t("notifications.policyVersion")}
                    </FieldLabel>
                    <PolicyVersionField
                      id="meta-version"
                      value={versionName}
                      disabled={!canManage || versionLocked}
                      onChange={(value) => {
                        setVersionName(value)
                        setMetaDirty(true)
                      }}
                    />
                    <FieldDescription>
                      {versionLocked
                        ? t("notifications.policyVersionLockedHint")
                        : t("notifications.policyVersionRenameHint")}
                    </FieldDescription>
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="meta-effective">
                      {t("notifications.policyEffective")}
                    </FieldLabel>
                    <DatePicker
                      id="meta-effective"
                      value={effectiveDate}
                      disabled={!canManage}
                      onChange={(value) => {
                        setEffectiveDate(value ?? "")
                        setMetaDirty(true)
                      }}
                      minDate={monthsFromNow(-5)}
                      maxDate={monthsFromNow(5)}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="meta-note">
                      {t("notifications.policyChangeNote")}
                    </FieldLabel>
                    {/* The revision note is stored once per version, outside the
                        per-language payload — it is the admin's own prose, so it
                        follows the console rather than the edited locale. */}
                    <Textarea
                      id="meta-note"
                      rows={2}
                      placeholder={t("notifications.policyChangeNoteHint")}
                      value={changeNote}
                      disabled={!canManage}
                      onChange={(e) => {
                        setChangeNote(e.target.value)
                        setMetaDirty(true)
                      }}
                    />
                  </Field>
                </FieldGroup>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyHeader")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="doc-title">
                      {t("notifications.policyDocTitle")}
                    </FieldLabel>
                    <Input
                      id="doc-title"
                      dir={dir}
                      value={doc.title}
                      disabled={!canManage}
                      placeholder={t("notifications.policyDocTitlePlaceholder")}
                      onChange={(e) => patch({ title: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-effective">
                      {t("notifications.policyEffectiveLabel")}
                    </FieldLabel>
                    <Input
                      id="doc-effective"
                      dir={dir}
                      value={doc.effectiveDate}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policyEffectiveLabelPlaceholder"
                      )}
                      onChange={(e) => patch({ effectiveDate: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-versionlabel">
                      {t("notifications.policyVersionLabel")}
                    </FieldLabel>
                    <Input
                      id="doc-versionlabel"
                      dir={dir}
                      value={doc.versionLabel}
                      disabled={!canManage}
                      onChange={(e) => patch({ versionLabel: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-warning">
                      {t("notifications.policyDraftWarning")}
                    </FieldLabel>
                    <Textarea
                      id="doc-warning"
                      dir={dir}
                      rows={2}
                      value={doc.unfilledWarning}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policyDraftWarningPlaceholder"
                      )}
                      onChange={(e) => patch({ unfilledWarning: e.target.value })}
                    />
                  </Field>
                  <StringListEditor
                    dir={dir}
                    label={t("notifications.policyIntro")}
                    values={doc.intro}
                    disabled={!canManage}
                    removeLabel={t("notifications.policyRemoveParagraph")}
                    onChange={(intro) => patch({ intro })}
                  />
                </FieldGroup>
              </CardContent>
            </Card>

            <SectionListEditor
              dir={dir}
              label={t("notifications.policySections")}
              sections={doc.sections}
              disabled={!canManage}
              onChange={(sections) => patch({ sections })}
            />

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyRetention")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="ret-heading">
                      {t("notifications.policyHeading")}
                    </FieldLabel>
                    <Input
                      id="ret-heading"
                      dir={dir}
                      value={doc.retention.heading}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policyRetentionHeadingPlaceholder"
                      )}
                      onChange={(e) =>
                        patch({
                          retention: { ...doc.retention, heading: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="ret-intro">
                      {t("notifications.policyParagraphs")}
                    </FieldLabel>
                    <Textarea
                      id="ret-intro"
                      dir={dir}
                      rows={3}
                      value={doc.retention.intro}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policyRetentionIntroPlaceholder"
                      )}
                      onChange={(e) =>
                        patch({
                          retention: { ...doc.retention, intro: e.target.value },
                        })
                      }
                    />
                  </Field>

                  <Field>
                    <FieldLabel>{t("notifications.policyColumns")}</FieldLabel>
                    <div className="grid gap-2 sm:grid-cols-3">
                      {doc.retention.columns.map((column, index) => (
                        <Input
                          key={index}
                          dir={dir}
                          value={column}
                          disabled={!canManage}
                          placeholder={retentionColumnPlaceholders[index]}
                          onChange={(e) => {
                            const columns = [...doc.retention.columns] as [
                              string,
                              string,
                              string,
                            ]
                            columns[index] = e.target.value
                            patch({ retention: { ...doc.retention, columns } })
                          }}
                        />
                      ))}
                    </div>
                  </Field>

                  <Field>
                    <FieldLabel>{t("notifications.policyRows")}</FieldLabel>
                    <div className="flex flex-col gap-3">
                      {doc.retention.rows.map((row, index) => (
                        <div
                          key={index}
                          className="grid items-start gap-2 rounded-md border p-3 sm:grid-cols-[1fr_1fr_1.5fr_auto]"
                        >
                          {(["category", "retention", "detail"] as const).map(
                            (key) => (
                              <Textarea
                                key={key}
                                dir={dir}
                                rows={2}
                                value={row[key]}
                                disabled={!canManage}
                                placeholder={retentionRowPlaceholders[key]}
                                onChange={(e) => {
                                  const rows = [...doc.retention.rows]
                                  rows[index] = { ...rows[index], [key]: e.target.value }
                                  patch({ retention: { ...doc.retention, rows } })
                                }}
                              />
                            )
                          )}
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            disabled={!canManage}
                            aria-label={t("notifications.policyRemoveRow")}
                            title={t("notifications.policyRemoveRow")}
                            onClick={() =>
                              patch({
                                retention: {
                                  ...doc.retention,
                                  rows: doc.retention.rows.filter(
                                    (_, i) => i !== index
                                  ),
                                },
                              })
                            }
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      ))}
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="w-fit"
                        disabled={!canManage}
                        onClick={() =>
                          patch({
                            retention: {
                              ...doc.retention,
                              rows: [
                                ...doc.retention.rows,
                                { category: "", retention: "", detail: "" },
                              ],
                            },
                          })
                        }
                      >
                        <Plus data-icon="inline-start" />
                        {t("notifications.policyAddRow")}
                      </Button>
                    </div>
                  </Field>
                </FieldGroup>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyDeletion")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="del-heading">
                      {t("notifications.policyHeading")}
                    </FieldLabel>
                    <Input
                      id="del-heading"
                      dir={dir}
                      value={doc.deletion.heading}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policyDeletionHeadingPlaceholder"
                      )}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, heading: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <StringListEditor
                    dir={dir}
                    label={t("notifications.policyParagraphs")}
                    values={doc.deletion.paragraphs}
                    disabled={!canManage}
                    removeLabel={t("notifications.policyRemoveParagraph")}
                    onChange={(paragraphs) =>
                      patch({ deletion: { ...doc.deletion, paragraphs } })
                    }
                  />
                  <StringListEditor
                    dir={dir}
                    label={t("notifications.policyBullets")}
                    values={doc.deletion.bullets}
                    disabled={!canManage}
                    rows={2}
                    removeLabel={t("notifications.policyRemoveBullet")}
                    onChange={(bullets) =>
                      patch({ deletion: { ...doc.deletion, bullets } })
                    }
                  />
                  <Field>
                    <FieldLabel htmlFor="del-button">
                      {t("notifications.policyButtonLabel")}
                    </FieldLabel>
                    <Input
                      id="del-button"
                      dir={dir}
                      value={doc.deletion.button}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policyButtonLabelPlaceholder"
                      )}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, button: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="del-hint">
                      {t("notifications.policySignedInHint")}
                    </FieldLabel>
                    <Input
                      id="del-hint"
                      dir={dir}
                      value={doc.deletion.signedInHint}
                      disabled={!canManage}
                      placeholder={t(
                        "notifications.policySignedInHintPlaceholder"
                      )}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, signedInHint: e.target.value },
                        })
                      }
                    />
                  </Field>
                </FieldGroup>
              </CardContent>
            </Card>

            <SectionListEditor
              dir={dir}
              label={t("notifications.policyRights")}
              sections={doc.rights}
              disabled={!canManage}
              onChange={(rights) => patch({ rights })}
            />

            <SectionListEditor
              dir={dir}
              label={t("notifications.policyClosing")}
              sections={doc.closing}
              disabled={!canManage}
              onChange={(closing) => patch({ closing })}
            />

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyContactLabels")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  {(
                    [
                      [
                        "contactDpoLabel",
                        t("notifications.policyDpoLabel"),
                        t("notifications.policyDpoLabelPlaceholder"),
                      ],
                      [
                        "contactVerbisLabel",
                        t("notifications.policyVerbisLabel"),
                        t("notifications.policyVerbisLabelPlaceholder"),
                      ],
                      [
                        "contactKepLabel",
                        t("notifications.policyKepLabel"),
                        t("notifications.policyKepLabelPlaceholder"),
                      ],
                    ] as const
                  ).map(([key, label, placeholder]) => (
                    <Field key={key}>
                      <FieldLabel htmlFor={`doc-${key}`}>{label}</FieldLabel>
                      <Input
                        id={`doc-${key}`}
                        dir={dir}
                        value={doc[key]}
                        disabled={!canManage}
                        placeholder={placeholder}
                        onChange={(e) => patch({ [key]: e.target.value })}
                      />
                    </Field>
                  ))}
                </FieldGroup>
              </CardContent>
            </Card>
          </div>

          {/* Live preview — same renderer as the public page. It scrolls as its
              own column now; the sticky offset it used before was standing in
              for exactly that. */}
          <div className="xl:min-h-0 xl:min-w-0 xl:flex-1 xl:overflow-y-auto">
            <PolicyPreviewPane
              content={doc}
              disclosure={disclosure}
              dir={dir}
              version={version}
              // The disclosure fields default to "" while this query is in
              // flight, which reads as "unfilled" and flashed the red banner on
              // every load of the editor. The page's own skeleton gate watches
              // the content query, not this one.
              controllerStatus={publishedQuery.isPending ? "pending" : "known"}
            />
          </div>
        </div>
      )}
    </div>
  )
}
