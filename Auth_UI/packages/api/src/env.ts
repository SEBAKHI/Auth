/**
 * Runtime environment configuration.
 *
 * The API base URL is injected at build time via Vite (`VITE_API_BASE_URL`).
 * In development it defaults to the local Auth API. In production it must be
 * provided so the SPA points at the deployed API origin.
 */
const rawBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "https://localhost:5101"

/** Absolute base URL of the Auth API, without a trailing slash. */
export const API_BASE_URL = rawBaseUrl.replace(/\/+$/, "")

const rawAccountsUrl =
  import.meta.env.VITE_ACCOUNTS_URL ?? "https://localhost:5174"

/**
 * Absolute origin of the accounts app (end-user self-service), without a
 * trailing slash. Used by the console to hand off user-facing flows
 * (profile, invitations).
 */
export const ACCOUNTS_URL = rawAccountsUrl.replace(/\/+$/, "")

/**
 * Google OAuth client id for "Continue with Google" (must match the API's
 * Google audience). Empty disables the button.
 */
export const GOOGLE_CLIENT_ID: string =
  import.meta.env.VITE_GOOGLE_CLIENT_ID ?? ""

/**
 * Apple Services ID for "Continue with Apple" (must match the API's Apple
 * audience). Empty disables the button.
 */
export const APPLE_SERVICES_ID: string =
  import.meta.env.VITE_APPLE_SERVICES_ID ?? ""
