import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Check, CheckCircle2, Loader2, Plus, Save, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate, useParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { directionForLanguage, SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { Alert, AlertDescription } from "@astoom/ui/alert"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@astoom/ui/card"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { PageHeader } from "@astoom/ui/common/page-header"
import {
  type PolicyDisclosure,
  type PrivacyPolicyContent,
} from "@astoom/ui/common/policy-document"
import { usePageBreadcrumb } from "@astoom/ui/crumbs"
import { Field, FieldDescription, FieldGroup, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import { useUnsavedChangesPrompt } from "@astoom/ui/hooks/use-unsaved-changes"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { Textarea } from "@astoom/ui/textarea"
import { PERMISSIONS } from "@/lib/constants"
import {
  SectionListEditor,
  StringListEditor,
} from "./components/policy-field-editors"
import { PolicyPreviewPane } from "./components/policy-preview-pane"
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
  const { version = "" } = useParams()

  const canManage = hasPermission(PERMISSIONS.privacyPolicy.manage)

  const [language, setLanguage] = React.useState("en")
  const [doc, setDoc] = React.useState<PrivacyPolicyContent | null>(null)
  const [dirty, setDirty] = React.useState(false)
  const [parseError, setParseError] = React.useState<string | null>(null)
  const [pendingLanguage, setPendingLanguage] = React.useState<string | null>(null)
  const [versionName, setVersionName] = React.useState("")
  const [effectiveDate, setEffectiveDate] = React.useState("")
  const [changeNote, setChangeNote] = React.useState("")
  const [metaDirty, setMetaDirty] = React.useState(false)

  const navigate = useNavigate()
  const { onFocusCapture, insert } = useFocusedField()
  usePageBreadcrumb(version || undefined)

  const versionsQuery = useQuery({
    queryKey: ["privacy-policy-versions"],
    queryFn: () => unwrap(api.GET("/api/v1/privacy-policy/versions")),
  })
  const versionRow = versionsQuery.data?.find((v) => v.version === version)

  React.useEffect(() => {
    if (!versionRow || metaDirty) return
    setVersionName(versionRow.version ?? "")
    setEffectiveDate((versionRow.effectiveDateUtc ?? "").slice(0, 10))
    setChangeNote(versionRow.changeNote ?? "")
  }, [versionRow, metaDirty])

  const contentQuery = useQuery({
    queryKey: ["privacy-policy-content", version, language],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/privacy-policy/versions/content", {
          params: { query: { version, language } },
        })
      ),
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

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/privacy-policy/versions/content", {
          body: {
            version,
            languageCode: language,
            contentJson: JSON.stringify(doc),
          },
        })
      ),
    onSuccess: () => {
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-content"] })
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      toast.success(t("notifications.policyContentSaved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const metaMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/privacy-policy/versions", {
          body: {
            version,
            newVersion: versionName.trim() || null,
            effectiveDateUtc: effectiveDate + "T00:00:00Z",
            changeNote: changeNote.trim() || null,
          },
        })
      ),
    onSuccess: (saved) => {
      setMetaDirty(false)
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      toast.success(t("notifications.policyVersionSaved"))
      // A rename changes the route key; follow it so the page stays valid.
      if (saved?.version && saved.version !== version) {
        navigate("/notifications/policy/" + saved.version, { replace: true })
      }
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const publishMutation = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/v1/privacy-policy/versions/publish", { body: { version } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      toast.success(t("notifications.policyPublishedToast"))
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
    policyVersion: publishedQuery.data?.disclosure?.policyVersion ?? version,
  }

  // A published or already-announced revision's identifier is referenced by
  // deletion records and by users' inboxes, so it can no longer move.
  const versionLocked = Boolean(versionRow?.isPublished || versionRow?.notifiedAtUtc)

  const dir = directionForLanguage(language)

  return (
    <div className="space-y-6">
      {unsavedPrompt}
      <PageHeader
        title={t("notifications.policyContentTitle", { version })}
        description={t("notifications.policyContentDescription")}
        actions={
          canManage ? (
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                disabled={!dirty || !doc || saveMutation.isPending}
                onClick={() => saveMutation.mutate()}
              >
                {saveMutation.isPending ? (
                  <Loader2 className="animate-spin" />
                ) : (
                  <Save data-icon="inline-start" />
                )}
                {t("common.save")}
              </Button>
              {versionRow && !versionRow.isPublished ? (
                <Button
                  disabled={dirty || publishMutation.isPending}
                  onClick={() => publishMutation.mutate()}
                >
                  <CheckCircle2 data-icon="inline-start" />
                  {t("notifications.policyPublish")}
                </Button>
              ) : null}
            </div>
          ) : undefined
        }
      />

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
        <div className="grid gap-6 xl:grid-cols-2">
          {/* Editor */}
          <div className="flex flex-col gap-6" onFocusCapture={onFocusCapture}>
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
                    <Input
                      id="meta-version"
                      dir="ltr"
                      value={versionName}
                      disabled={!canManage || versionLocked}
                      onChange={(e) => {
                        setVersionName(e.target.value)
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
                    <Input
                      id="meta-effective"
                      type="date"
                      value={effectiveDate}
                      disabled={!canManage}
                      onChange={(e) => {
                        setEffectiveDate(e.target.value)
                        setMetaDirty(true)
                      }}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="meta-note">
                      {t("notifications.policyChangeNote")}
                    </FieldLabel>
                    <Textarea
                      id="meta-note"
                      dir="auto"
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
                  {canManage ? (
                    <Button
                      variant="outline"
                      className="w-fit"
                      disabled={!metaDirty || metaMutation.isPending}
                      onClick={() => metaMutation.mutate()}
                    >
                      {metaMutation.isPending ? (
                        <Loader2 className="animate-spin" />
                      ) : null}
                      {t("notifications.policySaveVersion")}
                    </Button>
                  ) : null}
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
                      dir="auto"
                      value={doc.title}
                      disabled={!canManage}
                      onChange={(e) => patch({ title: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-effective">
                      {t("notifications.policyEffectiveLabel")}
                    </FieldLabel>
                    <Input
                      id="doc-effective"
                      dir="auto"
                      value={doc.effectiveDate}
                      disabled={!canManage}
                      onChange={(e) => patch({ effectiveDate: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-versionlabel">
                      {t("notifications.policyVersionLabel")}
                    </FieldLabel>
                    <Input
                      id="doc-versionlabel"
                      dir="auto"
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
                      dir="auto"
                      rows={2}
                      value={doc.unfilledWarning}
                      disabled={!canManage}
                      onChange={(e) => patch({ unfilledWarning: e.target.value })}
                    />
                  </Field>
                  <StringListEditor
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
                      dir="auto"
                      value={doc.retention.heading}
                      disabled={!canManage}
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
                      dir="auto"
                      rows={3}
                      value={doc.retention.intro}
                      disabled={!canManage}
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
                          dir="auto"
                          value={column}
                          disabled={!canManage}
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
                                dir="auto"
                                rows={2}
                                value={row[key]}
                                disabled={!canManage}
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
                      dir="auto"
                      value={doc.deletion.heading}
                      disabled={!canManage}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, heading: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <StringListEditor
                    label={t("notifications.policyParagraphs")}
                    values={doc.deletion.paragraphs}
                    disabled={!canManage}
                    removeLabel={t("notifications.policyRemoveParagraph")}
                    onChange={(paragraphs) =>
                      patch({ deletion: { ...doc.deletion, paragraphs } })
                    }
                  />
                  <StringListEditor
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
                      dir="auto"
                      value={doc.deletion.button}
                      disabled={!canManage}
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
                      dir="auto"
                      value={doc.deletion.signedInHint}
                      disabled={!canManage}
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
              label={t("notifications.policyRights")}
              sections={doc.rights}
              disabled={!canManage}
              onChange={(rights) => patch({ rights })}
            />

            <SectionListEditor
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
                      ["contactDpoLabel", t("notifications.policyDpoLabel")],
                      ["contactVerbisLabel", t("notifications.policyVerbisLabel")],
                      ["contactKepLabel", t("notifications.policyKepLabel")],
                    ] as const
                  ).map(([key, label]) => (
                    <Field key={key}>
                      <FieldLabel htmlFor={`doc-${key}`}>{label}</FieldLabel>
                      <Input
                        id={`doc-${key}`}
                        dir="auto"
                        value={doc[key]}
                        disabled={!canManage}
                        onChange={(e) => patch({ [key]: e.target.value })}
                      />
                    </Field>
                  ))}
                </FieldGroup>
              </CardContent>
            </Card>
          </div>

          {/* Live preview — same renderer as the public page */}
          <div className="xl:sticky xl:top-6 xl:self-start">
            <PolicyPreviewPane
              content={doc}
              disclosure={disclosure}
              dir={dir}
              version={version}
            />
          </div>
        </div>
      )}
    </div>
  )
}
