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
 * The key a pick of `item` lands at, the scope the server writes it at: a role or
 * permission an application owns goes to that application, anything else to the
 * platform scope (the pickers send no application). A picker offers an item
 * while this key is not assigned, so a legacy platform-wide grant of an
 * application's permission does not hide the grant that works; and a key the
 * server still lists is one staged for removal, which re-picking cancels (the
 * dialog's rule) instead of adding beside it.
 */
export function pickedAssignmentKey(item: {
  id: string
  applicationId?: string | null
}): string {
  return scopedAssignmentKey(item.id, item.applicationId)
}
