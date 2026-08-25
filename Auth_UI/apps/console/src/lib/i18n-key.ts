/**
 * Turns a stored code into the i18n key its display name is read under:
 * `external-login.linked` becomes `externalLoginLinked`.
 *
 * The separators have to go rather than be kept: i18next reads a dot as a path
 * into a nested object, so `auditLogs.actions.user.login` would look for a
 * `user` object rather than for the key it was given. The same trick the
 * settings fields already play — see `fieldI18nKey`.
 *
 * Shared because two catalogues need exactly this transform, on codes that
 * separate their words differently: audit actions with dots (`user.login`) and
 * notification types with hyphens (`new-device-sign-in`).
 */
export function codeI18nKey(code: string): string {
  return code
    .split(/[.\-_]+/)
    .filter(Boolean)
    .map((part, index) =>
      index === 0 ? part : part.charAt(0).toUpperCase() + part.slice(1)
    )
    .join("")
}
