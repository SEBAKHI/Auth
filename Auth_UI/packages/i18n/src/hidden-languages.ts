import * as React from "react"

import { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES, type LanguageCode } from "./languages"

/**
 * The languages an administrator hid from the language menus (platform settings › Languages):
 * a hidden language is not listed, its bundle is never fetched, and a page that would open in
 * it opens in English. Content editors (notification translations, design strings) still list
 * every language: they read `SUPPORTED_LANGUAGES`, never this.
 *
 * The list arrives with the platform's branding (`BrandingProvider` hands it here). A copy is
 * kept in this browser (per app: each origin has its own) and read when this module is
 * evaluated, so the language a page opens in is resolved against it before any bundle is
 * requested. A copy older than the change (a device that has never seen the branding, or saw
 * it before the language was hidden) cannot know: that one load may fetch the language, once;
 * the branding's answer then switches the page to English and every later load knows.
 *
 * English is never hidden (the server refuses it, and this ignores it), so at least one
 * language is always listed.
 */

export const HIDDEN_LANGUAGES_STORAGE_KEY = "auth.languages.hidden"

export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number]

/**
 * Known codes other than English, once each, in the menus' order: the form the store keeps and
 * the Languages tab compares and sends.
 */
export function normalizeHiddenLanguages(codes: readonly unknown[]): LanguageCode[] {
  return SUPPORTED_LANGUAGES.map((lang) => lang.code).filter(
    (code) => code !== DEFAULT_LANGUAGE && codes.includes(code)
  )
}

function readStored(): LanguageCode[] {
  try {
    const raw = localStorage.getItem(HIDDEN_LANGUAGES_STORAGE_KEY)
    const value: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(value) ? normalizeHiddenLanguages(value) : []
  } catch {
    // Storage is absent in privacy-restricted contexts, or the copy is unreadable: none hidden.
    return []
  }
}

function writeStored(codes: readonly LanguageCode[]): void {
  try {
    if (codes.length === 0) localStorage.removeItem(HIDDEN_LANGUAGES_STORAGE_KEY)
    else localStorage.setItem(HIDDEN_LANGUAGES_STORAGE_KEY, JSON.stringify(codes))
  } catch {
    // Not kept: the next load resolves without it until the branding answers again.
  }
}

function visibleFrom(hidden: readonly LanguageCode[]): readonly SupportedLanguage[] {
  return SUPPORTED_LANGUAGES.filter((lang) => !hidden.includes(lang.code))
}

let hidden: readonly LanguageCode[] = readStored()
let visible = visibleFrom(hidden)
const listeners = new Set<() => void>()

/**
 * Replaces the hidden languages with the branding's list (unknown codes and English are
 * ignored), keeps the copy for the next load, and tells every listener when it changed.
 */
export function setHiddenLanguages(codes: readonly string[] | null | undefined): void {
  const next = normalizeHiddenLanguages(codes ?? [])
  if (next.length === hidden.length && next.every((code, index) => code === hidden[index])) return

  hidden = next
  visible = visibleFrom(next)
  writeStored(next)
  for (const listener of listeners) listener()
}

/** The languages the menus list, in their order; English always among them. */
export function getVisibleLanguages(): readonly SupportedLanguage[] {
  return visible
}

export function isLanguageVisible(code: string): boolean {
  return visible.some((lang) => lang.code === code)
}

/** The language to show for `code`: itself while it is listed, English once it is hidden. */
export function visibleOrDefault(code: LanguageCode): LanguageCode {
  return isLanguageVisible(code) ? code : DEFAULT_LANGUAGE
}

/** Calls `listener` whenever the hidden languages change; returns the unsubscribe. */
export function subscribeHiddenLanguages(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** The languages a language menu lists, re-rendering when an administrator changes them. */
export function useVisibleLanguages(): readonly SupportedLanguage[] {
  return React.useSyncExternalStore(subscribeHiddenLanguages, getVisibleLanguages)
}
