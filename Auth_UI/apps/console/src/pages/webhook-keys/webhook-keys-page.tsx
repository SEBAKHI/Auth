import type { ColumnFiltersState } from "@tanstack/react-table"
import { useTranslation } from "react-i18next"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { DEFAULT_PAGE_SIZE } from "@/lib/constants"
import {
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { WEBHOOK_KEY_SORTABLE_COLUMNS } from "./webhook-key-columns"
import { WebhookKeysPanel } from "./webhook-keys-panel"
import {
  CREDENTIAL_KEY_URL_FILTERS,
  credentialColumnFilters,
  credentialFiltersFromColumns,
  type CredentialKeyListFilters,
} from "../credential-key-list-url"

const WEBHOOK_KEYS_LIST_URL_OPTIONS = {
  defaultPageSize: DEFAULT_PAGE_SIZE,
  sortableColumns: WEBHOOK_KEY_SORTABLE_COLUMNS,
  defaultSorting: [],
  filters: CREDENTIAL_KEY_URL_FILTERS,
} satisfies ListUrlStateOptions<CredentialKeyListFilters>

/** Every application's webhook keys, with a picker to narrow them to one. */
export function WebhookKeysPage() {
  const { t } = useTranslation()

  // undefined now means "every application", not "nothing chosen": the table
  // renders on arrival instead of waiting for a picker the reader may not even be
  // allowed to load (the picker needs applications:read; this page does not).
  const {
    search,
    sorting,
    filters,
    setSearch,
    setSorting,
    setFilter,
    setFilters,
  } = useListUrlState(WEBHOOK_KEYS_LIST_URL_OPTIONS)
  const { applicationId } = filters

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6">
      <WebhookKeysPanel
        applicationId={applicationId}
        header={(createButton) => (
          <>
            <PageHeader
              title={t("webhookKeys.title")}
              description={t("webhookKeys.subtitle")}
              actions={
                <div className="flex items-center gap-2">{createButton}</div>
              }
            />
            <div className="max-w-xs">
              <ApplicationSelect
                value={applicationId || undefined}
                onChange={(value) => setFilter("applicationId", value ?? "")}
                allowAll
                className="w-full"
              />
            </div>
          </>
        )}
        tableId="webhook-keys"
        fillHeight
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
