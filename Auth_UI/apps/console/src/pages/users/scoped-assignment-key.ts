/**
 * A user's role or direct permission is held *at a scope*: platform-wide, or
 * inside one application, and the same one may be held at both. The API removes
 * exactly the one named by id and scope, so an assignment chip carries both in
 * its key. Keyed by the id alone, every removal named the platform scope whatever
 * the chip showed: an application-scoped role was reported removed and stayed.
 */
export function scopedAssignmentKey(
  id: string,
  applicationId?: string | null
): string {
  return `${id}@${applicationId ?? ""}`
}

/** The id and scope a key names; no application is the platform scope. */
export function parseScopedAssignmentKey(key: string): {
  id: string
  applicationId?: string
} {
  const separator = key.indexOf("@")
  if (separator < 0) return { id: key }
  const applicationId = key.slice(separator + 1)
  return {
    id: key.slice(0, separator),
    applicationId: applicationId || undefined,
  }
}

/**
 * The ids held at any scope among `keys`. The pickers assign at the platform
 * scope only, and offer an id while it is held at no scope, which is what they
 * did when the key was the id.
 */
export function heldIds(keys: Iterable<string>): Set<string> {
  return new Set([...keys].map((key) => parseScopedAssignmentKey(key).id))
}

/**
 * The key a picker stages for `id`. An id is offered only while it is held at no
 * scope, so one the server still lists is staged for removal; re-picking it must
 * cancel that removal, the dialog's rule, not add a platform-wide assignment
 * beside one being taken away. Otherwise the new assignment is platform-wide.
 */
export function pickedAssignmentKey(
  serverKeys: readonly string[],
  id: string
): string {
  return (
    serverKeys.find((key) => parseScopedAssignmentKey(key).id === id) ??
    scopedAssignmentKey(id)
  )
}
