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

async function fetchBotDefenseConfig(): Promise<BotDefenseConfig> {
  return normalizeBotDefenseConfig(await unwrap(api.GET("/api/v1/Platform/bot-defense")))
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
    queryFn: fetchBotDefenseConfig,
    staleTime: BOT_DEFENSE_STALE_MS,
  })
  return {
    config: query.data,
    isError: query.isError,
    refetch: () => void query.refetch(),
  }
}

/**
 * The configuration for a submit that is about to go out: the cached answer,
 * or the one on its way. Undefined when it cannot be had; the server decides
 * then, and its refusal carries its own sentence.
 */
export async function ensureBotDefenseConfig(
  queryClient: QueryClient
): Promise<BotDefenseConfig | undefined> {
  try {
    return await queryClient.ensureQueryData({
      queryKey: BOT_DEFENSE_QUERY_KEY,
      queryFn: fetchBotDefenseConfig,
      staleTime: BOT_DEFENSE_STALE_MS,
    })
  } catch {
    return undefined
  }
}
