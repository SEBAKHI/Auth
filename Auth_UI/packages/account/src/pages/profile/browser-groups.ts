import type { Schemas } from "@authsystem/api/types"

type Device = Schemas["KnownDeviceDto"]
type Session = Schemas["SessionDto"]

export interface BrowserGroup {
  device: Device
  sessions: Session[]
}

export interface GroupedSessions {
  groups: BrowserGroup[]
  /**
   * Sessions that named no browser. A client with no storage to keep an
   * identifier in — an OAuth token exchange — or a browser the user has since
   * forgotten. They get their own heading rather than being hidden or filed
   * under a parent that did not actually start them.
   */
  unattributed: Session[]
}

/**
 * Partitions live sessions across the browsers that started them.
 *
 * The join is on the device row's id, which the server resolved from the
 * signature both records carry — an exact key match, not a guess from the user
 * agent. What that key identifies is a browser profile, though, not a machine:
 * two browsers on one computer are two groups, and clearing site data mints a
 * third. The caveat line in the UI exists for exactly that.
 */
export function groupSessionsByBrowser(
  devices: Device[],
  sessions: Session[]
): GroupedSessions {
  const byDevice = new Map<string, Session[]>()
  const unattributed: Session[] = []

  for (const session of sessions) {
    if (!session.knownDeviceId) {
      unattributed.push(session)
      continue
    }

    const existing = byDevice.get(session.knownDeviceId)
    if (existing) {
      existing.push(session)
    } else {
      byDevice.set(session.knownDeviceId, [session])
    }
  }

  const groups = devices.map((device) => ({
    device,
    sessions: (device.id && byDevice.get(device.id)) || [],
  }))

  // A session pointing at a device the list does not contain would otherwise
  // vanish from the page entirely. It cannot happen while both queries succeed
  // against the same data, but "silently disappears" is the wrong failure for a
  // security surface, so anything unmatched falls through to the visible bucket.
  const shown = new Set(groups.flatMap((g) => g.sessions.map((s) => s.id)))
  for (const session of sessions) {
    if (session.knownDeviceId && !shown.has(session.id)) {
      unattributed.push(session)
    }
  }

  return { groups, unattributed }
}
