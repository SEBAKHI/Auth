import { useQuery, type QueryClient } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"

/**
 * The bot check on the anonymous sign-in pages (S13): Cloudflare Turnstile.
 * `Off` offers nothing; `Monitor` sends a token when one is ready and never
 * waits; `Enforce` waits for the token before sending, because the server
 * refuses a request without one (400 `BotDefense.TokenMissing`).
 */
export type BotDefenseMode = "Off" | "Monitor" | "Enforce"

/**
 * What `GET /Platform/bot-defense` publishes. `siteKey` is null wherever there
 * is nothing to check (Off, no sitekey, or a testing key in Production): the
 * page then loads nothing from Cloudflare.
 */
export interface BotDefenseConfig {
  mode: BotDefenseMode
  siteKey: string | null
}

export const BOT_DEFENSE_QUERY_KEY = ["bot-defense"] as const

/** The server caches it a minute (max-age=60), and so does the page. */
const BOT_DEFENSE_STALE_MS = 60 * 1000

const MODES: readonly BotDefenseMode[] = ["Off", "Monitor", "Enforce"]

/** The answer, read strictly: an unknown mode is no check, an empty key none. */
export function normalizeBotDefenseConfig(raw: {
  mode?: unknown
  siteKey?: unknown
}): BotDefenseConfig {
  const mode = MODES.find((value) => value === raw.mode) ?? "Off"
  const siteKey =
    typeof raw.siteKey === "string" && raw.siteKey.trim() !== "" ? raw.siteKey.trim() : null
  return { mode, siteKey: mode === "Off" ? null : siteKey }
}

const OFF: BotDefenseConfig = { mode: "Off", siteKey: null }

/**
 * The server's answer. A 404 reads as Off: an API from before S13 (one rolled
 * back alone) has no such endpoint and checks nothing. `fresh` asks past the
 * browser's own cache (`max-age=60`), so a submit's read is never older than
 * the query's minute (REVIEW 167 C10).
 */
async function fetchBotDefenseConfig(fresh = false): Promise<BotDefenseConfig> {
  const answer = fresh
    ? await api.GET("/api/v1/Platform/bot-defense", { cache: "no-cache" })
    : await api.GET("/api/v1/Platform/bot-defense")
  if ((answer.error as { status?: number } | undefined)?.status === 404 || answer.response?.status === 404) {
    return OFF
  }
  return normalizeBotDefenseConfig(await unwrap(Promise.resolve(answer)))
}

/**
 * The live configuration, one cache entry for every page. No initial data: a
 * guessed "Off" would send a request without a token while the server
 * enforces, and a guessed sitekey would load Cloudflare's script for nothing.
 */
export function useBotDefenseConfig(): {
  config: BotDefenseConfig | undefined
  isError: boolean
  refetch: () => void
} {
  const query = useQuery({
    queryKey: BOT_DEFENSE_QUERY_KEY,
    queryFn: () => fetchBotDefenseConfig(),
    staleTime: BOT_DEFENSE_STALE_MS,
  })
  return {
    config: query.data,
    isError: query.isError,
    refetch: () => void query.refetch(),
  }
}

/**
 * The configuration for a submit that is about to go out: the cached answer
 * while it is under a minute old, else a fresh one, so a page left open hears
 * the owner switch the check off (the break-glass) at its next submit.
 * Undefined when it cannot be had; the server decides then, and its refusal
 * carries its own sentence.
 */
export async function ensureBotDefenseConfig(
  queryClient: QueryClient
): Promise<BotDefenseConfig | undefined> {
  try {
    return await queryClient.fetchQuery({
      queryKey: BOT_DEFENSE_QUERY_KEY,
      queryFn: () => fetchBotDefenseConfig(true),
      staleTime: BOT_DEFENSE_STALE_MS,
    })
  } catch {
    return undefined
  }
}
