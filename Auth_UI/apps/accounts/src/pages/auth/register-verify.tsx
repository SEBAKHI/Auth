import * as React from "react"
import { Navigate, useLocation, useNavigate } from "react-router-dom"

import { EmailCodeFlow } from "@authsystem/auth/pages/email-code-flow"
import { useReloadOnRestore } from "@authsystem/auth/reload-on-restore"

import {
  clearRegistrationFlow,
  readPendingRegistration,
  savePendingRegistration,
} from "./registration-flow"

/**
 * Second sign-up screen: the code that proves the mailbox, and whatever the
 * server says comes next — the sign-up's own fields, or, for an address that
 * already has an account, the sign-in it would have reached from the sign-in
 * page (OI-215). It is the sign-in page's code flow: one place decides what a
 * matched code leads to.
 *
 * The code was mailed by the request that brought the person here, so this
 * screen requests nothing on mount — a request now would rotate the code that
 * is still in transit. The identity comes from session storage, which is why a
 * reload lands back on this screen instead of at the start; with no identity at
 * all there is nothing to check, and the screen sends the person to the start.
 * The code itself is never stored: a reload after it matched asks for it again.
 *
 * Back to this page asks the server again rather than reviving it (OI-150);
 * the identity in session storage brings the reload back to this screen.
 */
export function RegisterVerifyPage() {
  useReloadOnRestore()
  const navigate = useNavigate()
  const { search } = useLocation()
  const [pending] = React.useState(readPendingRegistration)

  if (!pending) {
    return <Navigate to={{ pathname: "/register", search }} replace />
  }

  return (
    <EmailCodeFlow
      pending={pending}
      staySignedIn={false}
      requireFullProof={false}
      onPendingChange={savePendingRegistration}
      onEnded={clearRegistrationFlow}
      onChangeAddress={() => {
        clearRegistrationFlow()
        navigate({ pathname: "/register", search })
      }}
      // An account that must prove its password signs in on the sign-in page,
      // with the address it just proved.
      onPasswordRequired={(email) => {
        clearRegistrationFlow()
        navigate({ pathname: "/login", search }, { state: { email } })
      }}
    />
  )
}

/**
 * The former third screen's address. The fields it asked for are now asked on
 * the code screen once the code matched, and a matched code is never kept, so
 * a link or a reload here goes back to the code.
 */
export function RegisterCompleteRedirect() {
  const { search } = useLocation()
  return <Navigate to={{ pathname: "/register/verify", search }} replace />
}
