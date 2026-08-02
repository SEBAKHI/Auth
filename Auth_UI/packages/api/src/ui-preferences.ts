import { api } from "@authsystem/api/client"

/**
 * Self-service client display preferences (table column layouts today).
 *
 * The server treats every value as an opaque JSON string and enforces only a
 * key namespace, a size cap and a per-user key count — the shape belongs to
 * whoever writes it.
 */
export type UiPreferenceMap = Record<string, string>

/** Reads every preference the signed-in user holds. */
export async function fetchUiPreferences(): Promise<UiPreferenceMap> {
  const { data, error } = await api.GET("/api/v1/Users/me/ui-preferences")
  if (error || !data) return {}
  return data as UiPreferenceMap
}

/** Stores one preference. Resolves false when the write did not land. */
export async function putUiPreference(
  key: string,
  value: string
): Promise<boolean> {
  const { error } = await api.PUT("/api/v1/Users/me/ui-preferences/{key}", {
    params: { path: { key } },
    body: { value },
  })
  return !error
}

/** Removes one preference. */
export async function deleteUiPreference(key: string): Promise<boolean> {
  const { error } = await api.DELETE("/api/v1/Users/me/ui-preferences/{key}", {
    params: { path: { key } },
  })
  return !error
}
