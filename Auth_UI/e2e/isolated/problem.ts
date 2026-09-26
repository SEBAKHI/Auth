import type { Route } from "@playwright/test"

import type { PublishedErrorCode } from "../../packages/api/src/error-codes.generated"

/** The framework's titles, as the API's error contract leaves them (ADR 0001). */
const TITLES: Readonly<Record<number, string>> = {
  400: "Bad Request",
  401: "Unauthorized",
  403: "Forbidden",
  404: "Not Found",
  409: "Conflict",
  429: "Too Many Requests",
  500: "An error occurred while processing your request.",
  503: "Service Unavailable",
}

interface ProblemEntry {
  code: PublishedErrorCode
  pointer?: string
}

/**
 * An error body as the API writes it (ADR 0001): the framework's title, the
 * status, a published `code`, the catalog sentence in `detail`, a trace id, and
 * `errors` only for a Validation result with two or more failures - entries with
 * a code and a pointer, never text. Mocks build their errors here so they cannot
 * drift back to a shape the API no longer sends.
 */
export function problem(
  status: number,
  code: PublishedErrorCode,
  extra: { detail?: string; errors?: ProblemEntry[] } = {}
) {
  return {
    title: TITLES[status] ?? "Error",
    status,
    code,
    traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
    ...extra,
  }
}

/** Answers a mocked request with {@link problem}'s body, as problem+json. */
export async function fulfillProblem(route: Route, body: ReturnType<typeof problem>) {
  await route.fulfill({
    status: body.status,
    contentType: "application/problem+json",
    body: JSON.stringify(body),
  })
}
