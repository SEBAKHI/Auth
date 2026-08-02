import { ExternalLink } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@authsystem/ui/collapsible"
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemTitle,
} from "@authsystem/ui/item"

type AppPlatform = "ios" | "android" | "desktop" | "web"

interface AuthenticatorApp {
  /** Product name — never translated. */
  name: string
  platforms: readonly AppPlatform[]
  url: string
}

/**
 * The suggested apps, as module constants.
 *
 * Deliberately NOT server-provided: this list renders on sign-in surfaces, so a
 * value that could be influenced remotely would be a way to put an attacker's
 * download link in front of someone mid-authentication.
 *
 * Selection criteria — free, available on more than one platform, encrypted
 * backup or export, and not bound to a phone number. All four read the standard
 * `otpauth://` URI this server issues (RFC 6238 defaults: SHA-1, 6 digits, 30s),
 * so compatibility is not the discriminator; recoverability is.
 *
 * Authy is excluded on purpose: it ties the account to a phone number, its
 * desktop apps were discontinued, and it has no export — recommending it strands
 * users who later want to move.
 *
 * The links are vendor landing pages rather than per-store deep links, so one
 * URL is correct on every device and there is no per-store rot to maintain.
 */
const AUTHENTICATOR_APPS: readonly AuthenticatorApp[] = [
  {
    name: "Google Authenticator",
    platforms: ["ios", "android"],
    url: "https://safety.google/authentication/",
  },
  {
    name: "Microsoft Authenticator",
    platforms: ["ios", "android"],
    url: "https://www.microsoft.com/security/mobile-authenticator-app",
  },
  {
    name: "Ente Auth",
    platforms: ["ios", "android", "desktop", "web"],
    url: "https://ente.io/auth/",
  },
  {
    name: "Bitwarden Authenticator",
    platforms: ["ios", "android"],
    url: "https://bitwarden.com/products/authenticator/",
  },
]

function AppList() {
  const { t } = useTranslation()

  return (
    <ItemGroup>
      {AUTHENTICATOR_APPS.map((app) => (
        <Item key={app.name} role="listitem" variant="outline" size="sm">
          <ItemContent>
            <ItemTitle>{app.name}</ItemTitle>
            <ItemDescription>
              {app.platforms
                .map((platform) => t(`common.platform.${platform}`))
                .join(" · ")}
            </ItemDescription>
          </ItemContent>
          <ItemActions>
            <Button variant="link" size="sm" asChild>
              {/* noreferrer as well as noopener: these open from an
                  authentication screen, so the referrer is not the vendor's
                  business either. */}
              <a href={app.url} target="_blank" rel="noopener noreferrer">
                {t("common.view")}
                <ExternalLink data-icon="inline-end" />
              </a>
            </Button>
          </ItemActions>
        </Item>
      ))}
    </ItemGroup>
  )
}

/**
 * Suggested authenticator apps.
 *
 * `grid` is for the enrolment screen, where the choice is actually being made —
 * the list is open, with a line explaining what it is for.
 *
 * `disclosure` is for the login-time code screen, where the user has already
 * enrolled. A promotional block of download links on a code-entry page is
 * phishing-shaped and would train exactly the wrong reflex, so it stays folded
 * behind a "don't have your app?" trigger.
 */
export function AuthenticatorApps({
  variant = "grid",
}: {
  variant?: "grid" | "disclosure"
}) {
  const { t } = useTranslation()

  if (variant === "disclosure") {
    return (
      <Collapsible className="w-full">
        <CollapsibleTrigger asChild>
          <Button type="button" variant="link" className="text-muted-foreground">
            {t("auth.noAuthenticatorApp")}
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <AppList />
        </CollapsibleContent>
      </Collapsible>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted-foreground">
        {t("auth.authenticatorAppsHint")}
      </p>
      <AppList />
    </div>
  )
}
