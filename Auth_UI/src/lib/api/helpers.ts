/**
 * Unwraps an openapi-fetch result, throwing the API error so it can be caught
 * by React Query / try-catch and surfaced via `getErrorMessage`.
 */
export async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown }>
): Promise<T> {
  const { data, error } = await call
  if (error) throw error
  return data as T
}

/** Coerce the API's `number | string` numerics to a number for display/use. */
export function toNumber(value: number | string | null | undefined): number {
  if (value === null || value === undefined) return 0
  return typeof value === "string" ? Number(value) : value
}
