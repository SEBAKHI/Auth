import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

/**
 * Whose sign-in page designs: one application's (the "Templates" tab) or the platform's
 * (the platform settings card). The same screens serve both; only the endpoints and the
 * permission differ (applications:update, platform-settings:manage).
 */
export type DesignScope = { kind: "application"; applicationId: string } | { kind: "platform" }

export type LoginDesign = Schemas["LoginDesignDto"]
export type LoginDesignSummary = Schemas["LoginDesignSummaryDto"]
export type LoginDesignPreview = Schemas["LoginDesignPreviewDto"]

export interface DesignFields {
  name: string
  content: string
  stringsJson: string
  showPlatformLogo: boolean
}

/** The key of a scope's list; a design's own key extends it, so invalidating the list reaches both. */
export function designsKey(scope: DesignScope) {
  return ["login-designs", scope.kind === "platform" ? "platform" : scope.applicationId] as const
}

export function designKey(scope: DesignScope, designId: string) {
  return [...designsKey(scope), designId] as const
}

/**
 * A scope's designs. Anything but a list is refused as a failure, so a wrong answer shows
 * the card's error instead of taking the page around it down.
 */
export async function listDesigns(scope: DesignScope) {
  const designs = await (scope.kind === "platform"
    ? unwrap(api.GET("/api/v1/admin/platform-settings/login-designs"))
    : unwrap(
        api.GET("/api/v1/applications/{applicationId}/login-designs", {
          params: { path: { applicationId: scope.applicationId } },
        })
      ))
  if (!Array.isArray(designs)) throw new Error("The sign-in page designs could not be read.")
  return designs
}

export function getDesign(scope: DesignScope, designId: string) {
  return scope.kind === "platform"
    ? unwrap(
        api.GET("/api/v1/admin/platform-settings/login-designs/{designId}", {
          params: { path: { designId } },
        })
      )
    : unwrap(
        api.GET("/api/v1/applications/{applicationId}/login-designs/{designId}", {
          params: { path: { applicationId: scope.applicationId, designId } },
        })
      )
}

/** A new design; with no content the server starts it from its starter design. */
export function createDesign(scope: DesignScope, name: string) {
  const body = { name, content: null, stringsJson: null, showPlatformLogo: true }
  return scope.kind === "platform"
    ? unwrap(api.POST("/api/v1/admin/platform-settings/login-designs", { body }))
    : unwrap(
        api.POST("/api/v1/applications/{applicationId}/login-designs", {
          params: { path: { applicationId: scope.applicationId } },
          body,
        })
      )
}

export function saveDesign(
  scope: DesignScope,
  designId: string,
  fields: DesignFields,
  expectedRevision: string
) {
  const body = { ...fields, expectedRevision }
  return scope.kind === "platform"
    ? unwrap(
        api.PUT("/api/v1/admin/platform-settings/login-designs/{designId}", {
          params: { path: { designId } },
          body,
        })
      )
    : unwrap(
        api.PUT("/api/v1/applications/{applicationId}/login-designs/{designId}", {
          params: { path: { applicationId: scope.applicationId, designId } },
          body,
        })
      )
}

export function copyDesign(scope: DesignScope, designId: string, name: string) {
  return scope.kind === "platform"
    ? unwrap(
        api.POST("/api/v1/admin/platform-settings/login-designs/{designId}/copy", {
          params: { path: { designId } },
          body: { name },
        })
      )
    : unwrap(
        api.POST("/api/v1/applications/{applicationId}/login-designs/{designId}/copy", {
          params: { path: { applicationId: scope.applicationId, designId } },
          body: { name },
        })
      )
}

export function setDesignActive(scope: DesignScope, designId: string, active: boolean) {
  if (scope.kind === "platform") {
    return active
      ? unwrap(
          api.POST("/api/v1/admin/platform-settings/login-designs/{designId}/activate", {
            params: { path: { designId } },
          })
        )
      : unwrap(
          api.POST("/api/v1/admin/platform-settings/login-designs/{designId}/deactivate", {
            params: { path: { designId } },
          })
        )
  }
  const path = { applicationId: scope.applicationId, designId }
  return active
    ? unwrap(api.POST("/api/v1/applications/{applicationId}/login-designs/{designId}/activate", { params: { path } }))
    : unwrap(api.POST("/api/v1/applications/{applicationId}/login-designs/{designId}/deactivate", { params: { path } }))
}

export async function deleteDesign(scope: DesignScope, designId: string) {
  if (scope.kind === "platform") {
    await unwrap(
      api.DELETE("/api/v1/admin/platform-settings/login-designs/{designId}", {
        params: { path: { designId } },
      })
    )
    return
  }
  await unwrap(
    api.DELETE("/api/v1/applications/{applicationId}/login-designs/{designId}", {
      params: { path: { applicationId: scope.applicationId, designId } },
    })
  )
}

export function previewDesign(
  scope: DesignScope,
  input: { content: string; stringsJson: string; language: string }
) {
  return scope.kind === "platform"
    ? unwrap(api.POST("/api/v1/admin/platform-settings/login-designs/preview", { body: input }))
    : unwrap(
        api.POST("/api/v1/applications/{applicationId}/login-designs/preview", {
          params: { path: { applicationId: scope.applicationId } },
          body: input,
        })
      )
}
