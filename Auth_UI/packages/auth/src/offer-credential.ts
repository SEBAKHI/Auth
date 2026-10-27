/**
 * Offers a new credential to the browser's password manager.
 *
 * A sign-up completes as a single-page transition, and not every password
 * manager notices a form that was never "submitted" to a new document. The
 * Credential Management API says it outright, where supported; elsewhere the
 * real, read-only username field beside the password field is what the
 * heuristics key on. Best effort by definition: a refusal here is not a failed
 * sign-up.
 */
export async function offerCredential(email: string, password: string): Promise<void> {
  if (typeof window === "undefined" || !navigator.credentials?.store) return
  // Chromium-only and absent from the DOM typings, so it is looked up rather
  // than named: where the constructor is missing there is nothing to offer.
  const Ctor = (
    window as unknown as {
      PasswordCredential?: new (data: {
        id: string
        password: string
        name?: string
      }) => Credential
    }
  ).PasswordCredential
  if (!Ctor) return
  try {
    await navigator.credentials.store(new Ctor({ id: email, password, name: email }))
  } catch {
    // Declined, unsupported in this context, or a policy refusal: fine.
  }
}
