import type { ColumnFiltersState } from "@tanstack/react-table"
import { useTranslation } from "react-i18next"

import { DEFAULT_PAGE_SIZE } from "@/lib/constants"
import {
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { API_KEY_SORTABLE_COLUMNS } from "../api-keys/api-key-columns"
import { ApiKeysPanel } from "../api-keys/api-keys-panel"
import { WEBHOOK_KEY_SORTABLE_COLUMNS } from "../webhook-keys/webhook-key-columns"
import { WebhookKeysPanel } from "../webhook-keys/webhook-keys-panel"
import {
  CREDENTIAL_KEY_COLUMN_URL_FILTERS,
  credentialColumnFilters,
  credentialFiltersFromColumns,
  type CredentialKeyColumnFilters,
} from "../credential-key-list-url"
import { TabHeader } from "./application-tab-header"

/*
 * An application's own API keys and webhook keys (OI-264): the same tables as
 * the main-menu pages, fixed to the open application. The application column
 * starts hidden (every row is this application), and the URL state carries a
 * namespace so a tab's search, sort and chips never meet the page's `tab`
 * parameter or another tab's.
 */

const APPLICATION_API_KEYS_URL_OPTIONS = {
  namespace: "apiKeys",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: API_KEY_SORTABLE_COLUMNS,
  defaultSorting: [],
  filters: CREDENTIAL_KEY_COLUMN_URL_FILTERS,
} satisfies ListUrlStateOptions<CredentialKeyColumnFilters>

const APPLICATION_WEBHOOK_KEYS_URL_OPTIONS = {
  namespace: "webhookKeys",
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: WEBHOOK_KEY_SORTABLE_COLUMNS,
  defaultSorting: [],
  filters: CREDENTIAL_KEY_COLUMN_URL_FILTERS,
} satisfies ListUrlStateOptions<CredentialKeyColumnFilters>

export function ApplicationApiKeysTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const { search, sorting, filters, setSearch, setSorting, setFilters } =
    useListUrlState(APPLICATION_API_KEYS_URL_OPTIONS)

  return (
    <div className="flex flex-col gap-4">
      <ApiKeysPanel
        applicationId={appId}
        header={(createButton) => (
          <TabHeader
            description={t("apiKeys.subtitle")}
            createButton={createButton}
          />
        )}
        tableId="app-api-keys"
        defaultHidden={["applicationName"]}
        search={search}
        onSearchChange={setSearch}
        sorting={sorting}
        onSortingChange={setSorting}
        columnFilters={credentialColumnFilters(filters)}
        onColumnFiltersChange={(next: ColumnFiltersState) =>
          setFilters(credentialFiltersFromColumns(next))
        }
      />
    </div>
  )
}

export function ApplicationWebhookKeysTab({ appId }: { appId: string }) {
  const { t } = useTranslation()
  const { search, sorting, filters, setSearch, setSorting, setFilters } =
    useListUrlState(APPLICATION_WEBHOOK_KEYS_URL_OPTIONS)

  return (
    <div className="flex flex-col gap-4">
      <WebhookKeysPanel
        applicationId={appId}
        header={(createButton) => (
          <TabHeader
            description={t("webhookKeys.subtitle")}
            createButton={createButton}
          />
        )}
        tableId="app-webhook-keys"
        defaultHidden={["applicationName"]}
        search={search}
        onSearchChange={setSearch}
        sorting={sorting}
        onSortingChange={setSorting}
        columnFilters={credentialColumnFilters(filters)}
        onColumnFiltersChange={(next: ColumnFiltersState) =>
          setFilters(credentialFiltersFromColumns(next))
        }
      />
    </div>
  )
}
