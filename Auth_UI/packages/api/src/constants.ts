/** Default server-side page size for paged list endpoints (API caps at 100). */
export const DEFAULT_PAGE_SIZE = 20

/**
 * The shortest password the server can ever be configured to accept
 * (`Password:MinimumLength` bottoms out here), so anything below it is
 * rejectable locally without a round trip.
 *
 * It is a floor, NOT the live policy: the configured minimum is usually higher
 * (8 out of the box) and only the server knows it. Forms check this, submit,
 * and let the server's answer — which names the real minimum — be the
 * authority. Do not "helpfully" raise this to 8; that is the guess this
 * constant exists to stop each form from making on its own.
 */
export const PASSWORD_LENGTH_FLOOR = 6
