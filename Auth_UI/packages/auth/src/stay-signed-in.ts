import * as React from "react"

/**
 * The sign-in page's "Stay signed in" box, for the provider buttons it renders
 * under its form (OI-154).
 *
 * A reader, called at the moment a button signs in, not a value: the Google
 * button hands its callback to Google's script once, so a value captured at
 * render would be stale, and re-registering the callback on every tick of the
 * box would reload the button and spend a fresh nonce. Anywhere else the
 * buttons render (registration, account recovery) it reads false: those
 * sign-ins are browser sessions.
 */
const StaySignedInContext = React.createContext<() => boolean>(() => false)

export const StaySignedInProvider = StaySignedInContext.Provider

/** Reads the "Stay signed in" choice of the sign-in page around this component. */
export function useStaySignedIn(): () => boolean {
  return React.useContext(StaySignedInContext)
}
