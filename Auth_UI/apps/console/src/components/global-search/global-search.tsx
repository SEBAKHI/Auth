import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import {
  ArrowRight,
  ChevronDown,
  FileText,
  Search,
  SlidersHorizontal,
  TriangleAlert,
  X,
  type LucideIcon,
} from "lucide-react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { Button } from "@authsystem/ui/button"
import {
  Command,
  CommandDialog,
  CommandFooter,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandItemContent,
  CommandItemCrumb,
  CommandItemCrumbSeparator,
  CommandItemDescription,
  CommandItemTitle,
  CommandItemTrail,
  CommandList,
  CommandSeparator,
} from "@authsystem/ui/command"
import { Kbd } from "@authsystem/ui/kbd"
import { Skeleton } from "@authsystem/ui/skeleton"
import { useDebouncedValue } from "@authsystem/ui/hooks/use-debounced-value"
import { SEARCH_QUERY_PARAM } from "@authsystem/ui/hooks/use-search-query"

import { PERMISSIONS } from "@/lib/constants"
import { SETTINGS_QUERY_KEY } from "@/pages/system-settings/lib/sections"
import {
  buildSearchIndex,
  searchIndex,
  type FieldEntry,
  type RecordEntry,
  type SearchEntry,
  type SurfaceEntry,
} from "./build-index"
import { Highlight } from "./highlight"
import { MIN_RECORD_QUERY, recordIcon } from "./record-sources"
import { useRecentSearches, type RecentEntry } from "./use-recent-searches"
import { useRecordSearch } from "./use-record-search"

/** Where an admin with no history is offered a way in. */
const JUMP_TO = ["users", "applications", "section:Password", "audit-logs"]

/** A stable identity, so "nothing is expanded" does not re-run the search. */
const NO_GROUPS: ReadonlySet<string> = new Set()

/**
 * Long enough that a fast typist issues one round of requests instead of eight,
 * short enough that pausing to read feels like the results were already there.
 * Pages and settings ignore it entirely — they are in memory.
 */
const RECORD_DEBOUNCE_MS = 250

/**
 * Wraps a value in FSI…PDI so quoting it inside a sentence cannot reorder the
 * sentence: a Latin config key echoed back inside Arabic copy otherwise drags
 * the words around it to the wrong side.
 */
function isolate(value: string): string {
  return `⁨${value}⁩`
}

/** ⌘ on a Mac, Ctrl everywhere else — the hint has to match the key that works. */
function useCommandKeyLabel(): string {
  return React.useMemo(() => {
    const platform =
      typeof navigator === "undefined"
        ? ""
        : navigator.userAgent + (navigator.platform ?? "")
    return /Mac|iPhone|iPad|iPod/.test(platform) ? "⌘K" : "Ctrl K"
  }, [])
}

/** True while the user is typing somewhere the shortcut must not be stolen. */
function isTypingTarget(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null
  if (!element?.tagName) return false
  return (
    element.isContentEditable ||
    element.tagName === "INPUT" ||
    element.tagName === "TEXTAREA" ||
    element.tagName === "SELECT"
  )
}

/** The location of a result, as crumbs rather than a pre-joined string. */
function Trail({ crumbs }: { crumbs: string[] }) {
  return (
    <CommandItemTrail>
      {crumbs.map((crumb, index) => (
        <React.Fragment key={`${crumb}-${index}`}>
          {index > 0 ? <CommandItemCrumbSeparator /> : null}
          <CommandItemCrumb first={index === 0}>{crumb}</CommandItemCrumb>
        </React.Fragment>
      ))}
    </CommandItemTrail>
  )
}

/**
 * A page or section: its name, and under it the trail that says which one it
 * is. The page's own description is indexed and searched but not shown — a
 * third line of text per row is what turns a list into a wall.
 */
function SurfaceRow({
  entry,
  query,
  onSelect,
}: {
  entry: SurfaceEntry
  query: string
  onSelect: () => void
}) {
  return (
    <CommandItem value={entry.id} onSelect={onSelect}>
      <FileText aria-hidden="true" />
      <CommandItemContent>
        <CommandItemTitle>
          <Highlight text={entry.title} query={query} />
        </CommandItemTitle>
        {entry.trail.length > 0 ? <Trail crumbs={entry.trail} /> : null}
      </CommandItemContent>
    </CommandItem>
  )
}

/**
 * One setting. The hint wraps to a second line rather than being cut: it is
 * the sentence that decides whether this is the row you wanted.
 *
 * A row that matched only on its config key says so instead — otherwise it
 * sits unmarked in a list of highlighted rows and reads as a bug.
 */
function FieldRow({
  entry,
  query,
  onSelect,
}: {
  entry: FieldEntry
  query: string
  onSelect: () => void
}) {
  const { t } = useTranslation()
  const explainKey = entry.via === "keywords"

  return (
    <CommandItem value={entry.id} onSelect={onSelect}>
      <SlidersHorizontal aria-hidden="true" />
      <CommandItemContent>
        <CommandItemTitle>
          <Highlight text={entry.title} query={query} />
        </CommandItemTitle>
        {explainKey ? (
          <CommandItemDescription className="text-xs/5">
            {t("globalSearch.matchedOn")}{" "}
            {/* A config key is never Arabic; isolated so it cannot flip the
                line it sits in, and truncated on its own terms. */}
            <bdi dir="ltr" className="font-mono">
              {entry.configPath}
            </bdi>
          </CommandItemDescription>
        ) : entry.description ? (
          <CommandItemDescription>
            <Highlight text={entry.description} query={query} />
          </CommandItemDescription>
        ) : null}
      </CommandItemContent>
    </CommandItem>
  )
}

/**
 * One record. The second line is what tells two of them apart — an address, a
 * code, the application a role belongs to — so it is always shown, and it is
 * highlighted too: for a record, the match is as often in the address as in the
 * name.
 */
function RecordRow({
  entry,
  icon: Icon,
  query,
  onSelect,
}: {
  entry: RecordEntry
  icon: LucideIcon
  query: string
  onSelect: () => void
}) {
  return (
    <CommandItem value={entry.id} onSelect={onSelect}>
      <Icon aria-hidden="true" />
      <CommandItemContent>
        <CommandItemTitle>
          <Highlight text={entry.title} query={query} />
        </CommandItemTitle>
        {entry.description ? (
          <CommandItemDescription>
            {/* Addresses, codes and channel names are Latin sitting in a row
                that may be Arabic. Isolated, they order correctly instead of
                dragging the separators around them to the wrong side. */}
            <bdi>
              <Highlight text={entry.description} query={query} />
            </bdi>
          </CommandItemDescription>
        ) : null}
      </CommandItemContent>
    </CommandItem>
  )
}

/** Row geometry, held while a payload is still in flight. */
function LoadingRows({ rows = 3 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-1 p-1.5">
      {Array.from({ length: rows }, (_, row) => (
        <div key={row} className="flex items-center gap-2 px-3 py-2">
          <Skeleton className="size-4 shrink-0 rounded-sm" />
          <div className="flex min-w-0 flex-1 flex-col gap-1.5">
            <Skeleton className="h-3.5 w-40" />
            <Skeleton className="h-3 w-64" />
          </div>
        </div>
      ))}
    </div>
  )
}

/**
 * Searches the whole console: pages, records and settings, from one field.
 *
 * Three kinds of answer, in the order they can be produced. Pages and settings
 * are an in-memory index built from the navigation table and the settings
 * registry, so they answer on the first keystroke. Records — users, roles,
 * applications, organizations, notification templates and layouts — come from
 * the API, one debounced request per source, and land underneath as they
 * arrive.
 *
 * A centred palette rather than a dropdown hanging off the header button: it
 * answers to ⌘K from anywhere, and a panel anchored to a control that may be
 * scrolled out of reach — or that flips to the other side of the screen in
 * Arabic — is not the same thing as a global search. Being a real dialog also
 * means focus is trapped rather than tabbing away behind the open panel, and
 * Escape, focus-in and focus-return come for free.
 *
 * Lives in the console rather than the shared UI package because every result
 * is permission-filtered, which needs the auth context and the console's
 * permission constants.
 */
export function GlobalSearch() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { hasPermission, user } = useAuth()
  const [open, setOpen] = React.useState(false)
  const [query, setQuery] = React.useState("")
  // Keyed by the query that produced it, so a new query drops the expansion
  // without an effect that would re-render just to reset state.
  const [expansion, setExpansion] = React.useState<{
    query: string
    ids: ReadonlySet<string>
  }>({ query: "", ids: NO_GROUPS })
  const commandKey = useCommandKeyLabel()
  const { recent, remember, clear } = useRecentSearches(user?.id)

  const canReadSettings = hasPermission(PERMISSIONS.systemSettings.manage)

  // Fetched only once the panel opens, and under the settings page's own key,
  // so opening the search warms that page rather than duplicating its request.
  const settings = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/admin/system-settings")),
    enabled: open && canReadSettings,
  })

  const index = React.useMemo(
    // Every label in the index comes from `t`, whose identity changes on a
    // language switch — so the index rebuilds in the new language on its own.
    () => buildSearchIndex(settings.data?.sections ?? [], t, hasPermission),
    [settings.data, t, hasPermission]
  )

  const expanded = expansion.query === query ? expansion.ids : NO_GROUPS
  // No debounce here: the index is in memory, so a delay buys nothing and only
  // makes typing feel behind. Records are the one thing that waits.
  const results = React.useMemo(
    () => searchIndex(index, query, expanded),
    [index, query, expanded]
  )

  const debouncedQuery = useDebouncedValue(query, RECORD_DEBOUNCE_MS)
  const records = useRecordSearch({
    query: debouncedQuery,
    enabled: open,
    hasPermission,
  })

  const byId = React.useMemo(
    () => new Map(index.map((entry) => [entry.id, entry])),
    [index]
  )

  const jumpEntries = React.useMemo(
    () =>
      JUMP_TO.map((id) => byId.get(id)).filter((entry): entry is SearchEntry =>
        Boolean(entry)
      ),
    [byId]
  )

  const isLoadingIndex = canReadSettings && settings.isLoading
  // The records half is still working when the debounce has not caught up
  // either — otherwise a fast typist sees "nothing matches" for the instant
  // between the last keystroke and the request going out.
  const isSettlingRecords =
    records.isPending || (query.trim() !== debouncedQuery.trim())
  const hasResults = results.total > 0 || records.total > 0
  const totalShown = results.total + records.total

  // Every close resets, however it was closed. A palette that reopens holding
  // the last query never shows what you last opened, which is the one thing
  // you are most likely to want next.
  const close = () => {
    setOpen(false)
    setQuery("")
    setExpansion({ query: "", ids: NO_GROUPS })
  }

  const go = (entry: SearchEntry) => {
    remember({
      id: entry.id,
      route: entry.route,
      // Only a record needs its text carried: everything else is resolved from
      // the live index, so it follows a language switch and a revoked
      // permission instead of being frozen at the moment it was opened.
      ...(entry.kind === "record"
        ? { label: entry.title, sublabel: entry.description }
        : {}),
    })
    close()
    void navigate(entry.route)
  }

  /** Hands the query off to the page that owns the rest of the matches. */
  const goToList = (listRoute: string) => {
    close()
    void navigate(
      `${listRoute}?${SEARCH_QUERY_PARAM}=${encodeURIComponent(query.trim())}`
    )
  }

  const expand = (groupId: string) =>
    setExpansion((previous) => ({
      query,
      ids: new Set(previous.query === query ? previous.ids : []).add(groupId),
    }))

  const onOpenChange = (next: boolean) => {
    if (next) setOpen(true)
    else close()
  }

  // ⌘K / Ctrl+K from anywhere, so the search does not depend on reaching for
  // the header first. Ignored while the user is typing in another field —
  // the shortcut opening a palette mid-sentence is worse than not having it.
  React.useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== "k" || !(event.metaKey || event.ctrlKey)) {
        return
      }
      if (!open && isTypingTarget(event.target)) return
      event.preventDefault()
      setOpen((previous) => !previous)
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [open])

  // Spoken once the typing settles, so a screen reader is not read a running
  // count on every keystroke.
  const status = !query
    ? ""
    : isLoadingIndex || isSettlingRecords
      ? t("globalSearch.searching")
      : hasResults
        ? t("globalSearch.a11yResultCount", { total: totalShown })
        : t("globalSearch.a11yNoResults", { query: isolate(query) })
  const announced = useDebouncedValue(status, 400)

  /** Renders whichever row kind an entry is. Used by results and by history. */
  const renderEntry = (entry: SearchEntry, highlight: string) => {
    if (entry.kind === "surface") {
      return (
        <SurfaceRow
          key={entry.id}
          entry={entry}
          query={highlight}
          onSelect={() => go(entry)}
        />
      )
    }
    if (entry.kind === "field") {
      return (
        <FieldRow
          key={entry.id}
          entry={entry}
          query={highlight}
          onSelect={() => go(entry)}
        />
      )
    }
    return (
      <RecordRow
        key={entry.id}
        entry={entry}
        icon={recordIcon(entry.id) ?? FileText}
        query={highlight}
        onSelect={() => go(entry)}
      />
    )
  }

  /**
   * A remembered row. Pages and settings are re-resolved from the live index so
   * they stay current; a record falls back to the name stored with it, and is
   * dropped if it has none rather than rendering as an empty row.
   */
  const renderRecent = (item: RecentEntry) => {
    const indexed = byId.get(item.id)
    if (indexed) return renderEntry(indexed, "")
    if (!item.label) return null

    const entry: RecordEntry = {
      kind: "record",
      id: item.id,
      sourceKey: "",
      title: item.label,
      description: item.sublabel ?? "",
      route: item.route,
      keywords: "",
    }
    return (
      <RecordRow
        key={entry.id}
        entry={entry}
        icon={recordIcon(entry.id) ?? FileText}
        query=""
        onSelect={() => go(entry)}
      />
    )
  }

  const recentRows = recent.map(renderRecent).filter(Boolean)

  return (
    <>
      <Button
        variant="outline"
        size="sm"
        aria-label={t("globalSearch.label")}
        onClick={() => setOpen(true)}
        // Reads as the search field it stands in for, so it carries the
        // weight and colour of placeholder text rather than of a button.
        // The width is claimed from `md` up only: every other control in the
        // header is `shrink-0`, so a fixed width on a phone comes straight out
        // of the breadcrumb's share and leaves the page title nowhere to go.
        // Below that it collapses to the icon alone, as the table's column
        // button does.
        className="justify-start font-normal text-muted-foreground md:w-48 lg:w-64"
      >
        <Search data-icon="inline-start" />
        <span className="hidden truncate md:inline">
          {t("globalSearch.placeholder")}
        </span>
        <Kbd className="ms-auto hidden lg:inline-flex">{commandKey}</Kbd>
      </Button>

      <CommandDialog
        open={open}
        onOpenChange={onOpenChange}
        title={t("globalSearch.label")}
        description={t("globalSearch.hint")}
        // The first Escape empties the field; only an already-empty field
        // closes. Retyping a long key name because you meant to narrow it is
        // the kind of loss a palette should never impose.
        onEscapeKeyDown={(event) => {
          if (!query) return
          event.preventDefault()
          setQuery("")
        }}
      >
        {/* Scoring is tiered and also matches raw config paths, neither of
            which cmdk's built-in filter can express — and records are filtered
            by the server, which it cannot express at all.
            `disablePointerSelection` stops a stationary cursor from stealing
            the highlight away from the arrow keys as the list scrolls. */}
        <Command
          shouldFilter={false}
          disablePointerSelection
          label={t("globalSearch.label")}
        >
          <CommandInput
            value={query}
            onValueChange={setQuery}
            placeholder={t("globalSearch.placeholder")}
            aria-describedby="global-search-instructions"
          />
          <span id="global-search-instructions" className="sr-only">
            {t("globalSearch.a11yInstructions")}
          </span>
          <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
            {announced}
          </div>

          {/* `scroll-py` clears the sticky headings, so arrowing onto the first
              row of a group does not park it underneath one. The cap is a
              non-multiple of the row pitch on purpose: a half-visible last row
              is what says the list continues. `min-h-0` lets the dialog's own
              cap win on a short screen. */}
          <CommandList className="max-h-[24.5rem] min-h-0 scroll-py-10">
            {settings.isError ? (
              <div className="flex flex-col items-start gap-2 px-4 py-6">
                <p className="flex items-center gap-2 text-sm text-foreground">
                  <TriangleAlert className="size-4 text-muted-foreground" />
                  {t("globalSearch.settingsFailed")}
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => void settings.refetch()}
                >
                  {t("globalSearch.retry")}
                </Button>
              </div>
            ) : null}

            {isLoadingIndex ? <LoadingRows /> : null}

            {/* Idle groups are independent. History is context; quick
                navigation is the stable way in and must not disappear after
                the first remembered selection. */}
            {!query && !isLoadingIndex && recentRows.length > 0 ? (
              <CommandGroup heading={t("globalSearch.recent")}>
                {recentRows}
                {/* Carries an icon like every other row: a text-only row
                    starts inside the gutter the others align to and reads as
                    a stray line rather than a choice. */}
                <CommandItem value="recent:clear" onSelect={clear}>
                  <X aria-hidden="true" />
                  <span className="font-normal text-muted-foreground">
                    {t("globalSearch.clearRecent")}
                  </span>
                </CommandItem>
              </CommandGroup>
            ) : null}

            {!query && !isLoadingIndex && jumpEntries.length > 0 ? (
              <>
                {recentRows.length > 0 ? <CommandSeparator alwaysRender /> : null}
                <CommandGroup heading={t("globalSearch.jumpTo")}>
                  {jumpEntries.map((entry) => renderEntry(entry, ""))}
                </CommandGroup>
              </>
            ) : null}

            {/* Nothing matched. Not four grey words: say what was searched for,
                say what else to try, and leave a way out of the dead end. */}
            {query &&
            !isLoadingIndex &&
            !isSettlingRecords &&
            !hasResults &&
            !settings.isError ? (
              <div className="flex flex-col items-start gap-2 px-4 py-6">
                <p className="text-sm font-medium text-foreground">
                  {t("globalSearch.noResultsFor", { query: isolate(query) })}
                </p>
                <p className="text-sm text-muted-foreground">
                  {/* `min`, not `count`: i18next treats `count` as a plural
                      selector and would look for keys that do not exist. */}
                  {query.trim().length < MIN_RECORD_QUERY
                    ? t("globalSearch.minQueryHint", { min: MIN_RECORD_QUERY })
                    : t("globalSearch.emptySuggestion")}
                </p>
              </div>
            ) : null}

            {query && results.surfaces.length > 0 ? (
              <CommandGroup
                heading={
                  <>
                    <span>{t("globalSearch.pages")}</span>
                    {results.totalSurfaces > results.surfaces.length ? (
                      <span className="ms-auto font-normal">
                        {t("globalSearch.countOf", {
                          shown: results.surfaces.length,
                          total: results.totalSurfaces,
                        })}
                      </span>
                    ) : null}
                  </>
                }
              >
                {results.surfaces.map((entry) => (
                  <SurfaceRow
                    key={entry.id}
                    entry={entry}
                    query={query}
                    onSelect={() => go(entry)}
                  />
                ))}
              </CommandGroup>
            ) : null}

            {/* Records sit between the two: more specific than a page, and the
                answer whenever what was typed is a name rather than a concept. */}
            {query && records.groups.length > 0 ? (
              <>
                {results.surfaces.length > 0 ? (
                  <CommandSeparator alwaysRender />
                ) : null}
                {records.groups.map((group) => (
                  <CommandGroup
                    key={group.sourceKey}
                    heading={
                      <>
                        <span className="truncate">{t(group.headingKey)}</span>
                        {group.totalEntries > group.entries.length ? (
                          <span className="ms-auto shrink-0 font-normal">
                            {t("globalSearch.countOf", {
                              shown: group.entries.length,
                              total: group.totalEntries,
                            })}
                          </span>
                        ) : null}
                      </>
                    }
                  >
                    {group.entries.map((entry) => (
                      <RecordRow
                        key={entry.id}
                        entry={entry}
                        icon={group.icon}
                        query={query}
                        onSelect={() => go(entry)}
                      />
                    ))}
                    {/* The rest of the matches are a page away, not gone: the
                        query travels with the navigation and the list arrives
                        already filtered.

                        Carries no count, unlike the heading above it. The
                        heading counts what this panel matched; the page runs
                        its own search across columns the panel never showed,
                        and lands on a different number. Promising one here and
                        showing another there is the kind of small lie that
                        makes a reader stop trusting the rest. */}
                    {group.totalEntries > group.entries.length ? (
                      <CommandItem
                        value={`more:${group.sourceKey}`}
                        onSelect={() => goToList(group.listRoute)}
                      >
                        {/* An arrow means "onward", not "rightward". Lucide
                            glyphs do not mirror themselves, so it is turned by
                            hand — the same way every other directional icon in
                            the UI package is. */}
                        <ArrowRight aria-hidden="true" className="rtl:rotate-180" />
                        <span className="font-normal text-muted-foreground">
                          {t("globalSearch.seeAllIn", {
                            section: t(group.headingKey),
                          })}
                        </span>
                      </CommandItem>
                    ) : null}
                  </CommandGroup>
                ))}
              </>
            ) : null}

            {/* Records are still on the wire. Skeletons rather than nothing:
                without them the panel visibly reflows a moment after every
                pause, under a cursor that has already moved. */}
            {query && isSettlingRecords && records.groups.length === 0 &&
            query.trim().length >= MIN_RECORD_QUERY ? (
              <LoadingRows rows={2} />
            ) : null}

            {/* One source failing is not the search failing: the others have
                already rendered above, so this admits the gap without taking
                the panel over. */}
            {query && records.isError ? (
              <div className="flex items-center gap-2 px-4 py-3">
                <TriangleAlert className="size-4 shrink-0 text-muted-foreground" />
                <span className="text-sm text-muted-foreground">
                  {t("globalSearch.recordsFailed")}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  className="ms-auto"
                  onClick={records.retry}
                >
                  {t("globalSearch.retry")}
                </Button>
              </div>
            ) : null}

            {query &&
            (results.surfaces.length > 0 || records.groups.length > 0) &&
            results.fieldGroups.length > 0 ? (
              // A rule only where the kind of result changes. `alwaysRender`
              // because cmdk otherwise hides every separator the moment a query
              // exists — which is the only time this one has anything to divide.
              <CommandSeparator alwaysRender />
            ) : null}

            {query &&
              results.fieldGroups.map((group) => (
                <CommandGroup
                  key={group.sectionId}
                  heading={
                    <>
                      {group.sectionTrail.map((crumb, position) => (
                        <React.Fragment key={`${crumb}-${position}`}>
                          {position > 0 ? <CommandItemCrumbSeparator /> : null}
                          <span className="truncate">{crumb}</span>
                        </React.Fragment>
                      ))}
                      {group.totalFields > group.fields.length ? (
                        <span className="ms-auto shrink-0 font-normal">
                          {t("globalSearch.countOf", {
                            shown: group.fields.length,
                            total: group.totalFields,
                          })}
                        </span>
                      ) : null}
                    </>
                  }
                >
                  {group.fields.map((entry) => (
                    <FieldRow
                      key={entry.id}
                      entry={entry}
                      query={query}
                      onSelect={() => go(entry)}
                    />
                  ))}
                  {group.totalFields > group.fields.length ? (
                    <CommandItem
                      value={`more:${group.sectionId}`}
                      onSelect={() => expand(group.sectionId)}
                    >
                      <ChevronDown aria-hidden="true" />
                      <span className="font-normal text-muted-foreground">
                        {t("globalSearch.showAll", {
                          total: group.totalFields,
                        })}
                      </span>
                    </CommandItem>
                  ) : null}
                </CommandGroup>
              ))}
          </CommandList>

          <CommandFooter>
            <span className="flex items-center gap-1.5">
              <Kbd>↑</Kbd>
              <Kbd>↓</Kbd>
              {t("globalSearch.kbdNavigate")}
            </span>
            <span className="flex items-center gap-1.5">
              <Kbd>↵</Kbd>
              {t("globalSearch.kbdOpen")}
            </span>
            <span className="flex items-center gap-1.5">
              <Kbd>esc</Kbd>
              {t("globalSearch.kbdClose")}
            </span>
          </CommandFooter>
        </Command>
      </CommandDialog>
    </>
  )
}
