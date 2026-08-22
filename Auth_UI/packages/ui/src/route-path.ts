/**
 * Builds a URL path whose interpolated values are percent-encoded.
 *
 * Used as a tagged template so the encoding cannot be forgotten:
 *
 * ```ts
 * routePath`/users/${id}` // -> "/users/2f0c%2Fb1"
 * ```
 *
 * React Router's own `href()` does NOT encode - it substitutes the raw value
 * into the pattern - so a record id carrying `/`, `?` or `#` would silently
 * become extra path segments, a query string, or a fragment. `matchPath`
 * decodes each segment on the way back in (and restores an encoded `/`), so an
 * encoded id round-trips through `useParams()` unchanged.
 *
 * Route knowledge does not live here. This module owns the mechanism; each app
 * owns its own map of records to routes on top of it.
 */
export function routePath(
  strings: TemplateStringsArray,
  ...values: Array<string | number>
): string {
  return strings.reduce(
    (path, chunk, index) =>
      index < values.length
        ? path + chunk + encodeURIComponent(String(values[index]))
        : path + chunk,
    ""
  )
}
