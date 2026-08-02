import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import {
  ChevronDown,
  FileText,
  Search,
  SlidersHorizontal,
  TriangleAlert,
  X,
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

import { PERMISSIONS } from "@/lib/constants"
import { SETTINGS_QUERY_KEY } from "@/pages/system-settings/lib/sections"
import {
  buildSearchIndex,
  searchSettings,
  type FieldEntry,
  type SearchEntry,
  type SurfaceEntry,
} from "./build-index"
import { Highlight } from "./highlight"
import { useRecentSettings } from "./use-recent-settings"

/** Where an admin with no history is offered a way in. */
const JUMP_TO = [
  "section:Password",
  "section:Session",
  "secrets",
  "profile-security",
]

/** A stable identity, so "nothing is expanded" does not re-run the search. */
const NO_GROUPS: ReadonlySet<string> = new Set()

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
            {t("settingsSearch.matchedOn")}{" "}
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

/** Row geometry, held while the settings payload is still in flight. */
function LoadingRows() {
  return (
    <div className="flex flex-col gap-1 p-1.5">
      {[0, 1, 2].map((row) => (
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
 * Finds a setting by name without knowing which page it lives on.
 *
 * A centred palette rather than a dropdown hanging off the header button: it
 * answers to ⌘K from anywhere, and a panel anchored to a control that may be
 * scrolled out of reach — or that flips to the other side of the screen in
 * Arabic — is not the same thing as a global search. Being a real dialog also
 * means focus is trapped rather than tabbing away behind the open panel, and
 * Escape, focus-in and focus-return come for free.
 *
 * Lives in the console rather than the shared UI package because the results
 * are permission-filtered, which needs the auth context and the console's
 * permission constants.
 */
export function SettingsSearch() {
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
  const { recent, remember, clear } = useRecentSettings(user?.id)

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
  // No debounce: the index is in memory, so a delay buys nothing and only makes
  // typing feel behind. The announcement below is the one thing that waits.
  const results = React.useMemo(
    () => searchSettings(index, query, expanded),
    [index, query, expanded]
  )

  const byId = React.useMemo(
    () => new Map(index.map((entry) => [entry.id, entry])),
    [index]
  )

  const recentEntries = React.useMemo(
    () =>
      recent
        .map((item) => byId.get(item.id))
        .filter((entry): entry is SearchEntry => Boolean(entry)),
    [recent, byId]
  )

  const jumpEntries = React.useMemo(
    () =>
      JUMP_TO.map((id) => byId.get(id)).filter((entry): entry is SearchEntry =>
        Boolean(entry)
      ),
    [byId]
  )

  const isLoadingIndex = canReadSettings && settings.isLoading
  const hasResults = results.total > 0

  // Every close resets, however it was closed. A palette that reopens holding
  // the last query never shows what you last opened, which is the one thing
  // you are most likely to want next.
  const close = () => {
    setOpen(false)
    setQuery("")
    setExpansion({ query: "", ids: NO_GROUPS })
  }

  const go = (entry: SearchEntry) => {
    remember({ id: entry.id, route: entry.route })
    close()
    void navigate(entry.route)
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
    : isLoadingIndex
      ? t("settingsSearch.searching")
      : hasResults
        ? t("settingsSearch.a11yResultCount", { total: results.total })
        : t("settingsSearch.a11yNoResults", { query: isolate(query) })
  const announced = useDebouncedValue(status, 400)

  return (
    <>
      <Button
        variant="outline"
        size="sm"
        aria-label={t("settingsSearch.label")}
        onClick={() => setOpen(true)}
        // Reads as the search field it stands in for, so it carries the
        // weight and colour of placeholder text rather than of a button.
        className="w-48 justify-start font-normal text-muted-foreground lg:w-64"
      >
        <Search data-icon="inline-start" />
        <span className="truncate">{t("settingsSearch.placeholder")}</span>
        <Kbd className="ms-auto hidden lg:inline-flex">{commandKey}</Kbd>
      </Button>

      <CommandDialog
        open={open}
        onOpenChange={onOpenChange}
        title={t("settingsSearch.label")}
        description={t("settingsSearch.hint")}
        // The first Escape empties the field; only an already-empty field
        // closes. Retyping a long key name because you meant to narrow it is
        // the kind of loss a palette should never impose.
        onEscapeKeyDown={(event) => {
          if (!query) return
          event.preventDefault()
          setQuery("")
        }}
      >
        {/* Scoring is two-tier and also matches raw config paths, neither of
            which cmdk's built-in filter can express. `disablePointerSelection`
            stops a stationary cursor from stealing the highlight away from the
            arrow keys as the list scrolls under it. */}
        <Command
          shouldFilter={false}
          disablePointerSelection
          label={t("settingsSearch.label")}
        >
          <CommandInput
            value={query}
            onValueChange={setQuery}
            placeholder={t("settingsSearch.placeholder")}
            aria-describedby="settings-search-instructions"
          />
          <span id="settings-search-instructions" className="sr-only">
            {t("settingsSearch.a11yInstructions")}
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
                  {t("settingsSearch.loadFailed")}
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => void settings.refetch()}
                >
                  {t("settingsSearch.retry")}
                </Button>
              </div>
            ) : null}

            {isLoadingIndex ? <LoadingRows /> : null}

            {/* Idle: what you last opened, or a way in if there is no history.
                An empty panel with a sentence in it teaches nothing. */}
            {!query && !isLoadingIndex && recentEntries.length > 0 ? (
              <CommandGroup heading={t("settingsSearch.recent")}>
                {recentEntries.map((entry) =>
                  entry.kind === "surface" ? (
                    <SurfaceRow
                      key={entry.id}
                      entry={entry}
                      query=""
                      onSelect={() => go(entry)}
                    />
                  ) : (
                    <FieldRow
                      key={entry.id}
                      entry={entry}
                      query=""
                      onSelect={() => go(entry)}
                    />
                  )
                )}
                {/* Carries an icon like every other row: a text-only row
                    starts inside the gutter the others align to and reads as
                    a stray line rather than a choice. */}
                <CommandItem value="recent:clear" onSelect={clear}>
                  <X aria-hidden="true" />
                  <span className="font-normal text-muted-foreground">
                    {t("settingsSearch.clearRecent")}
                  </span>
                </CommandItem>
              </CommandGroup>
            ) : null}

            {!query && !isLoadingIndex && recentEntries.length === 0 &&
            jumpEntries.length > 0 ? (
              <CommandGroup heading={t("settingsSearch.jumpTo")}>
                {jumpEntries.map((entry) =>
                  entry.kind === "surface" ? (
                    <SurfaceRow
                      key={entry.id}
                      entry={entry}
                      query=""
                      onSelect={() => go(entry)}
                    />
                  ) : (
                    <FieldRow
                      key={entry.id}
                      entry={entry}
                      query=""
                      onSelect={() => go(entry)}
                    />
                  )
                )}
              </CommandGroup>
            ) : null}

            {/* Nothing matched. Not four grey words: say what was searched for,
                say what else to try, and leave a way out of the dead end. */}
            {query && !isLoadingIndex && !hasResults && !settings.isError ? (
              <div className="flex flex-col items-start gap-2 px-4 py-6">
                <p className="text-sm font-medium text-foreground">
                  {t("settingsSearch.noResultsFor", { query: isolate(query) })}
                </p>
                <p className="text-sm text-muted-foreground">
                  {t("settingsSearch.emptySuggestion")}
                </p>
              </div>
            ) : null}

            {query && results.surfaces.length > 0 ? (
              <CommandGroup
                heading={
                  <>
                    <span>{t("settingsSearch.pages")}</span>
                    {results.totalSurfaces > results.surfaces.length ? (
                      <span className="ms-auto font-normal">
                        {t("settingsSearch.countOf", {
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

            {query &&
            results.surfaces.length > 0 &&
            results.fieldGroups.length > 0 ? (
              // The one rule in the list, and only where the kind of result
              // changes. `alwaysRender` because cmdk otherwise hides every
              // separator the moment a query exists — which is the only time
              // this one has anything to divide.
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
                          {t("settingsSearch.countOf", {
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
                        {t("settingsSearch.showAll", {
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
              {t("settingsSearch.kbdNavigate")}
            </span>
            <span className="flex items-center gap-1.5">
              <Kbd>↵</Kbd>
              {t("settingsSearch.kbdOpen")}
            </span>
            <span className="flex items-center gap-1.5">
              <Kbd>esc</Kbd>
              {t("settingsSearch.kbdClose")}
            </span>
          </CommandFooter>
        </Command>
      </CommandDialog>
    </>
  )
}
