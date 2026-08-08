import { ProfileBrowsers } from "./profile-browsers"
import { ProfileLoginActivity } from "./profile-login-activity"

/**
 * The "sessions" tab.
 *
 * Two surfaces, deliberately not one. The browsers card is the short,
 * current-state list you act on — what is signed in, and what to end. The
 * activity card is the longer, read-only record of what has been tried,
 * successful and failed, which the first list cannot answer because a failed
 * attempt never becomes a session. Merging them would give one surface two
 * lengths and two interaction models.
 *
 * The export name and the tab key are unchanged, so the routes in both apps and
 * the console's command-palette deep links keep resolving.
 */
export function ProfileSessions() {
  return (
    <div className="flex flex-col gap-6">
      <ProfileBrowsers />
      <ProfileLoginActivity />
    </div>
  )
}
