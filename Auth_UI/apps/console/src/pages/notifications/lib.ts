import type { Schemas } from "@authsystem/api/types"

export type NotificationTemplateDto = Schemas["NotificationTemplateDto"]
export type NotificationTemplateDetailDto = Schemas["NotificationTemplateDetailDto"]
export type NotificationTemplateVersionDto = Schemas["NotificationTemplateVersionDto"]
export type NotificationTranslationDto = Schemas["NotificationTranslationDto"]
export type NotificationTypeDto = Schemas["NotificationTypeDto"]
export type NotificationLayoutDto = Schemas["NotificationLayoutDto"]
export type NotificationPreviewDto = Schemas["NotificationPreviewDto"]
export type NotificationsSummaryDto = Schemas["NotificationsSummaryDto"]

/** One entry of a notification type's variable catalog (VariablesJson). */
export interface TemplateVariable {
  name: string
  description?: string
  example?: string
  required?: boolean
  /**
   * Exact Liquid snippet inserted on click. Defaults to "{{ name }}"; layout
   * slots override it (e.g. "{{ content | raw }}").
   */
  insertText?: string
}

/**
 * The globals the renderer injects into EVERY template and layout render
 * (NotificationRenderingService.BuildModelAsync). Surfaced as their own palette
 * in both editors so authors see everything that is available instead of having
 * to guess — the type catalog only covers flow-specific variables.
 */
export function getRendererGlobals(t: (key: string) => string): TemplateVariable[] {
  return [
    { name: "Platform.Name", description: t("notifications.globalVarPlatformName"), example: "Acme" },
    { name: "Platform.LogoUrl", description: t("notifications.globalVarPlatformLogoUrl"), example: "https://auth.example.com/uploads/images/logo.webp" },
    { name: "Application.Name", description: t("notifications.globalVarApplicationName"), example: "Acme Console" },
    { name: "Application.Code", description: t("notifications.globalVarApplicationCode"), example: "console" },
    { name: "Application.BaseUrl", description: t("notifications.globalVarApplicationBaseUrl"), example: "https://accounts.example.com" },
    { name: "SenderName", description: t("notifications.layoutSlotSenderName"), example: "Acme" },
    { name: "Year", description: t("notifications.globalVarYear"), example: "2026" },
  ]
}

/**
 * Parses a type's VariablesJson catalog; malformed JSON degrades to an empty
 * palette rather than breaking the editor.
 */
export function parseVariables(variablesJson: string | null | undefined): TemplateVariable[] {
  if (!variablesJson) return []
  try {
    const parsed: unknown = JSON.parse(variablesJson)
    if (!Array.isArray(parsed)) return []
    return parsed.filter(
      (entry): entry is TemplateVariable =>
        typeof entry === "object" && entry !== null && typeof (entry as TemplateVariable).name === "string"
    )
  } catch {
    return []
  }
}

/** Editable state of one translation tab in the template editor. */
export interface TranslationDraft {
  subject: string
  bodyHtml: string
  bodyText: string
}

export function toTranslationDrafts(
  translations: NotificationTranslationDto[] | null | undefined
): Record<string, TranslationDraft> {
  const drafts: Record<string, TranslationDraft> = {}
  for (const translation of translations ?? []) {
    if (!translation.languageCode) continue
    drafts[translation.languageCode] = {
      subject: translation.subject ?? "",
      bodyHtml: translation.bodyHtml ?? "",
      bodyText: translation.bodyText ?? "",
    }
  }
  return drafts
}
