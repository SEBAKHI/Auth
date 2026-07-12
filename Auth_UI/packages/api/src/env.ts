/**
 * Runtime environment configuration.
 *
 * The API base URL is injected at build time via Vite (`VITE_API_BASE_URL`).
 * In development it defaults to the local Auth API. In production it must be
 * provided so the SPA points at the deployed API origin.
 */
const rawBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5100"

/** Absolute base URL of the Auth API, without a trailing slash. */
export const API_BASE_URL = rawBaseUrl.replace(/\/+$/, "")
