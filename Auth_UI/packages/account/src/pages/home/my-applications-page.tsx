import { useTranslation } from "react-i18next"

import {
  ApplicationTiles,
  useMyApplications,
} from "@authsystem/ui/common/application-tiles"
import { PageHeader } from "@authsystem/ui/common/page-header"

/**
 * Home (UI batch item 12): the applications the signed-in person can open, as
 * large tiles with the name under the logo. Mounted at `/` in the accounts app
 * and as "My applications" in the console.
 *
 * The server lists only applications the sign-in gate admits this person to and
 * that offer a way in (owner decision D9), each with its launch address. The
 * tiles, and the rule that drops a row whose address is not an https web
 * address, are the ones the header's grid button opens (`ApplicationTiles`).
 */
export function MyApplicationsPage() {
  const { t } = useTranslation()
  const state = useMyApplications()

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("myApplications.title")}
        description={t("myApplications.description")}
      />
      <ApplicationTiles state={state} label={t("myApplications.title")} />
    </div>
  )
}
