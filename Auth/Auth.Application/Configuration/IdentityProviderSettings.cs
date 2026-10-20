namespace Auth.Application.Configuration;

/// <summary>
/// Configuration for the identity-provider (universal login) features:
/// the authorization-code + PKCE flow and the IdP SSO session cookie.
/// </summary>
public class IdentityProviderSettings
{
    public const string SectionName = "IdentityProvider";

    /// <summary>
    /// Gets or sets the public base URL of the end-user accounts SPA
    /// (e.g. https://accounts.example.com). The authorize endpoint redirects
    /// unauthenticated users to its /login page.
    /// </summary>
    public string AccountsBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets this identity provider's own PUBLIC base URL as seen by
    /// browsers and consuming apps (e.g. https://auth.example.com). Behind a
    /// reverse proxy the request's Host header is the INTERNAL destination
    /// (e.g. identity.example.com), so any public URL the server emits — the
    /// authorize returnTo and the discovery-document endpoints — must be built
    /// from this configured value, never from Request.Host. Leave empty only
    /// where there is no proxy (local dev), where the request host is public.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Returns the configured <see cref="PublicBaseUrl"/> (trailing slash
    /// trimmed), or the supplied request-derived value when none is configured
    /// (proxy-less dev). Deterministic: it never depends on the reverse proxy's
    /// forwarded headers or address.
    /// </summary>
    /// <param name="requestFallback">e.g. $"{Request.Scheme}://{Request.Host}".</param>
    public string ResolvePublicBaseUrl(string requestFallback)
        => string.IsNullOrWhiteSpace(PublicBaseUrl) ? requestFallback : PublicBaseUrl.TrimEnd('/');

    /// <summary>
    /// Gets or sets the lifetime of one-time authorization codes in seconds.
    /// Kept short by design (OAuth 2.0 Security BCP recommends at most 60s).
    /// </summary>
    public int AuthorizationCodeLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the name of the HttpOnly IdP session cookie.
    /// </summary>
    public string IdpSessionCookieName { get; set; } = "auth_idp";

    /// <summary>
    /// Gets or sets the absolute lifetime of an IdP session in days, when the
    /// person ticked "Stay signed in": the cookie then outlives the browser for
    /// this long, and the session has no idle limit (OI-154).
    /// </summary>
    public int IdpSessionLifetimeDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets the absolute lifetime, in hours, of an IdP session signed in
    /// WITHOUT "Stay signed in" (a browser session): fixed at sign-in, never
    /// extended, and never above <see cref="IdpSessionLifetimeDays"/>. Its cookie
    /// has no expiry, so it also ends when the browser closes. 10 by default, as
    /// Keycloak's "SSO Session Max"; held to 1 to 24.
    /// </summary>
    public int IdpBrowserSessionLifetimeHours { get; set; } = 10;

    /// <summary>
    /// Gets or sets how long a browser session may go unused, in minutes, before
    /// it ends (OI-154, owner decision D-150-3 (a)): 30 by default, as Keycloak's
    /// "SSO Session Idle"; 0 turns the limit off; at most 1440, and a negative
    /// value reads as the default (<see cref="BrowserSessionIdleLimitFor"/>). Read on every
    /// use, so a change applies to existing sessions. A session dies idle lazily,
    /// when a read finds it, so raising the limit or switching it off would bring
    /// back the idle sessions nobody read since: a change saved in the console
    /// first ends them (<c>IIdpSessionIdleLimitChange</c>); a change in a settings
    /// file, applied at restart, does not, and can bring them back.
    /// </summary>
    public int IdpBrowserSessionIdleMinutes { get; set; } = DefaultIdpBrowserSessionIdleMinutes;

    /// <summary>
    /// The default of <see cref="IdpBrowserSessionIdleMinutes"/>, which a negative
    /// value also reads as.
    /// </summary>
    public const int DefaultIdpBrowserSessionIdleMinutes = 30;

    /// <summary>
    /// Gets or sets who decides whether a sign-in outlives the browser (OI-165):
    /// the person, through the sign-in page's "Stay signed in" box (the default),
    /// or the administrator for everyone, with the box hidden. Read only when a
    /// session is minted (<see cref="SignInIsPersistent"/>), so a change applies
    /// to new sign-ins only: an existing session keeps what it was given. One of
    /// the <see cref="StaySignedInPolicy"/> names, bound as text on purpose: the
    /// configuration binder throws on an enum name it cannot parse, and this
    /// section is read by every sign-in, so a typo in a file, or a value saved by
    /// a later version, would take the identity provider down. Read through
    /// <see cref="StaySignedInModeInForce"/>, which never throws.
    /// </summary>
    public string StaySignedInMode { get; set; } = nameof(StaySignedInPolicy.PersonChooses);

    /// <summary>
    /// Gets or sets the exact origins of this platform's own browser apps (the
    /// console and the accounts app), e.g. https://accounts.example.com. Kept
    /// apart from <c>Cors:AllowedOrigins</c> on purpose: CORS decides which pages
    /// may READ a response, this list decides which pages may SPEND the refresh
    /// cookie and sign in with a password. A request whose Origin is not listed
    /// never has the refresh cookie read, and — once the list is non-empty — is
    /// refused at <c>POST /auth/login</c>.
    /// <para>
    /// Empty by default and never initialized non-empty: the configuration binder
    /// appends to a non-empty initializer (see <see cref="SettingsArrayNormalizer"/>).
    /// Empty means "no first-party app is known", which is today's behaviour.
    /// </para>
    /// </summary>
    public string[] FirstPartySpaOrigins { get; set; } = [];

    /// <summary>
    /// Gets or sets whether a sign-in or refresh from a listed first-party origin
    /// delivers the refresh token in an HttpOnly cookie instead of the response
    /// body. On by default, and inert until <see cref="FirstPartySpaOrigins"/> is
    /// filled: no origin matches an empty list, so the body delivery is unchanged
    /// until the operator lists the apps, and from then on the cookie applies with
    /// no second switch to forget. Turning it off is the rollback: the next refresh
    /// returns the token in the body and clears the cookie.
    /// </summary>
    public bool SpaRefreshCookieEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the HttpOnly step-up cookie. It carries the
    /// signed record of a <c>prompt=login</c> demand across the login bounce so
    /// the server can tell that the re-authentication actually happened, rather
    /// than trusting the client to drop the parameter.
    /// </summary>
    public string StepUpCookieName { get; set; } = "auth_stepup";

    /// <summary>
    /// Gets or sets how long a step-up demand stays answerable, in minutes: the
    /// time the person has to finish SIGNING IN, from the moment the demand was
    /// raised, registration with an e-mailed code included (15 minutes for the
    /// code, the form, and a 5-minute second-factor challenge).
    /// </summary>
    /// <remarks>
    /// Two rules keep 30 minutes safe (OI-150). The evidence rule: only a session
    /// minted after the demand answers it, so an old sign-in never does. And the
    /// binding: a ticket answers only the request that raised it (its PKCE
    /// <c>code_challenge</c>), so a later sign-in request of the same application
    /// on the same browser cannot spend it. Without the binding, this window was
    /// the time in which the next person at a shared computer could enter the
    /// previous person's account. Held to 1 to 60 minutes.
    /// </remarks>
    public int StepUpTicketLifetimeMinutes { get; set; } = 30;

    /// <summary>
    /// Gets or sets how long after signing in a <c>prompt=login</c> demand still
    /// counts as answered, in minutes: the time to finish the steps that come
    /// AFTER the authentication (the organization step) before the person is
    /// asked to sign in again. Held to 1 to 60 minutes.
    /// </summary>
    /// <remarks>
    /// The second of two clocks, as mature identity providers keep them
    /// (Keycloak's "login action timeout", 5 minutes by default, beside its
    /// longer "login timeout"). <see cref="StepUpTicketLifetimeMinutes"/> bounds
    /// signing in; this bounds what follows it. It reads only the session's
    /// creation time, and applies only where <c>login</c> was demanded: plain
    /// single sign-on is untouched. A page left open, or brought back with the
    /// Back button, after this window asks for the password again.
    /// </remarks>
    public int StepUpFinishWindowMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets whether the authorize endpoint sends the browser to an accounts
    /// page with a short opaque handle (<c>?client_id=…&amp;auth_request=…</c>)
    /// instead of the whole authorize URL in <c>returnTo</c> (OI-145). A web
    /// application firewall reads a parameter holding an off-site <c>https://</c>
    /// address as a remote file inclusion and refuses the page.
    /// </summary>
    /// <remarks>
    /// On by default (owner decision D-145-1 (b)); the switch stays as a hot
    /// rollback lever. It only gates CREATING handles: a handle already issued
    /// resumes whatever this says, so turning it off strands nobody. An accounts
    /// build older than this change cannot read a handle, and a resume that needs an
    /// accounts page again keeps its handle, so a rollback turns this off, then waits
    /// <see cref="AuthorizationRequestLifetimeMinutes"/>, BEFORE restoring one.
    /// </remarks>
    public bool AuthorizationRequestHandlesEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a pending authorize request stays resumable, in
    /// minutes. Fixed when the request is stored, never extended. The default
    /// covers a registration with an e-mailed code.
    /// </summary>
    public int AuthorizationRequestLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets whether an explicit end of a browser's IdP session ends every
    /// sign-in born from it (single logout, OI-155): a sign-out there (the console,
    /// the accounts app, the end-session confirmation page, a server-side
    /// application's sign-out), or another person signing in on that browser, ends
    /// each application's and platform app's session whose refresh chain was born
    /// from that session, as revoking it would. On by default (owner decision
    /// D-155-3 (a)); read on every sign-out, so turning it off is the rollback, with
    /// no redeploy.
    /// </summary>
    /// <remarks>
    /// Only explicit ends, at once. A session that goes idle or reaches its maximum
    /// is not a sign-out (nothing recorded tells the two apart); the sign-ins born
    /// from it end at their next refresh instead, under
    /// <see cref="ApplicationSessionsEndWithSsoSession"/>. Off, a sign-out no longer
    /// ends the sign-ins born from the session at once, and the same person's next
    /// sign-in moves chains only from a live session, as before OI-155; except that a
    /// sign-out's own SSO-session lookup and revocation no longer stop for a caller
    /// that left. A sign-out still revokes the SSO session itself, so while
    /// <see cref="ApplicationSessionsEndWithSsoSession"/> is on each application still
    /// ends at its next refresh: the behaviour before OI-155 needs both switches off.
    /// </remarks>
    public bool SingleLogoutEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether an application's sign-in ends with the IdP session it
    /// was born from (the tree model, OI-155, D-155-9): once that session no longer
    /// signs the browser in (revoked, idle, or past its maximum), the application's
    /// next refresh is refused and its session ends, as a platform app's own already
    /// does (OI-154). The application's accepted refreshes count as uses of that
    /// session, so a person at work in it keeps it alive. On by default; read on
    /// every refresh, so turning it off is the rollback, with no redeploy.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SingleLogoutEnabled"/>: each can be turned off
    /// without the other. Off restores the behaviour before D-155-9: an
    /// application's chain lives its own refresh lifetime whatever its IdP session
    /// does (until a sign-out reaches it, while single logout is on); and, while
    /// single logout is on, it moves with the same person's next sign-in even from a
    /// session that is no longer live (with single logout off, chains move only from
    /// a live one). A chain with no IdP session refreshes as before either way.
    /// </remarks>
    public bool ApplicationSessionsEndWithSsoSession { get; set; } = true;

    /// <summary>
    /// Gets the step-up ticket lifetime as a TimeSpan, held to 1 to 60 minutes.
    /// </summary>
    /// <remarks>
    /// A file setting, so nothing else bounds it: a zero would expire every ticket
    /// the moment it was written, and every <c>prompt=login</c> would loop.
    /// </remarks>
    public TimeSpan StepUpTicketLifetime => TimeSpan.FromMinutes(Math.Clamp(StepUpTicketLifetimeMinutes, 1, 60));

    /// <summary>
    /// Gets the step-up finish window as a TimeSpan, held to 1 to 60 minutes.
    /// </summary>
    public TimeSpan StepUpFinishWindow => TimeSpan.FromMinutes(Math.Clamp(StepUpFinishWindowMinutes, 1, 60));

    /// <summary>
    /// Whether a sign-in that completed at <paramref name="authenticatedAtUtc"/>
    /// is still inside <see cref="StepUpFinishWindow"/> at <paramref name="nowUtc"/>.
    /// The one place the rule lives: the authorize endpoint and the organization
    /// step both ask it.
    /// </summary>
    /// <param name="authenticatedAtUtc">
    /// The IdP session's creation time: a session is minted only once the whole
    /// authentication, second factor included, has succeeded.
    /// </param>
    /// <param name="nowUtc">The current time.</param>
    public bool IsWithinStepUpFinishWindow(DateTime authenticatedAtUtc, DateTime nowUtc)
        => nowUtc - authenticatedAtUtc <= StepUpFinishWindow;

    /// <summary>
    /// Gets the pending authorize request lifetime as a TimeSpan, held to the
    /// range the console accepts (5 to 1440 minutes).
    /// </summary>
    /// <remarks>
    /// The console refuses a value outside the range, but a configuration file
    /// does not; a zero there would expire every request the moment it was
    /// stored and send every sign-in to the expired page.
    /// </remarks>
    public TimeSpan AuthorizationRequestLifetime =>
        TimeSpan.FromMinutes(Math.Clamp(AuthorizationRequestLifetimeMinutes, 5, 1440));

    /// <summary>
    /// Gets the authorization code lifetime as a TimeSpan.
    /// </summary>
    public TimeSpan AuthorizationCodeLifetime => TimeSpan.FromSeconds(AuthorizationCodeLifetimeSeconds);

    /// <summary>
    /// Gets the IdP session lifetime as a TimeSpan: that of a session signed in
    /// with "Stay signed in".
    /// </summary>
    public TimeSpan IdpSessionLifetime => TimeSpan.FromDays(IdpSessionLifetimeDays);

    /// <summary>
    /// Gets the browser-session idle limit as a TimeSpan (see
    /// <see cref="BrowserSessionIdleLimitFor"/>); zero means no limit.
    /// </summary>
    public TimeSpan IdpBrowserSessionIdleLimit => BrowserSessionIdleLimitFor(IdpBrowserSessionIdleMinutes);

    /// <summary>
    /// The idle limit a configured number of minutes means: at most 1440, and a
    /// negative value reads as the default. The registry's minimum guards only
    /// console changes, and a typo in a settings file (<c>-30</c>) must not
    /// switch the limit off, which is what 0 means.
    /// </summary>
    /// <param name="minutes">The configured value.</param>
    public static TimeSpan BrowserSessionIdleLimitFor(int minutes) =>
        TimeSpan.FromMinutes(minutes < 0 ? DefaultIdpBrowserSessionIdleMinutes : Math.Min(minutes, 1440));

    /// <summary>
    /// The absolute lifetime of a new IdP session: <see cref="IdpSessionLifetime"/>
    /// with "Stay signed in", otherwise <see cref="IdpBrowserSessionLifetimeHours"/>
    /// (held to 1 to 24), and never more than the first.
    /// </summary>
    /// <param name="persistent">Whether the person ticked "Stay signed in".</param>
    public TimeSpan IdpSessionLifetimeFor(bool persistent)
    {
        if (persistent)
        {
            return IdpSessionLifetime;
        }

        var browserSession = TimeSpan.FromHours(Math.Clamp(IdpBrowserSessionLifetimeHours, 1, 24));
        return browserSession < IdpSessionLifetime ? browserSession : IdpSessionLifetime;
    }

    /// <summary>
    /// The <see cref="StaySignedInMode"/> in force. A name is matched ignoring
    /// case; any value outside the three names, a name or a number, in a file or
    /// in the database, reads as <see cref="StaySignedInPolicy.PersonChooses"/>:
    /// the console validates the value, and a typo must fall back to the default
    /// behaviour (the box shows), never to either of the administrator's choices.
    /// </summary>
    public StaySignedInPolicy StaySignedInModeInForce =>
        Enum.TryParse<StaySignedInPolicy>(StaySignedInMode, ignoreCase: true, out var policy)
        && Enum.IsDefined(policy)
            ? policy
            : StaySignedInPolicy.PersonChooses;

    /// <summary>
    /// Whether the sign-in page offers the "Stay signed in" box: only while the
    /// person decides (<see cref="StaySignedInModeInForce"/>).
    /// </summary>
    public bool StaySignedInOffered => StaySignedInModeInForce == StaySignedInPolicy.PersonChooses;

    /// <summary>
    /// Whether a new sign-in outlives the browser, under <see cref="StaySignedInModeInForce"/>:
    /// the person's tick under <see cref="StaySignedInPolicy.PersonChooses"/>, never
    /// under <see cref="StaySignedInPolicy.AlwaysBrowserSession"/>, always under
    /// <see cref="StaySignedInPolicy.AlwaysStaySignedIn"/>.
    /// </summary>
    /// <param name="personTicked">Whether the person ticked "Stay signed in".</param>
    public bool SignInIsPersistent(bool personTicked) => StaySignedInModeInForce switch
    {
        StaySignedInPolicy.AlwaysBrowserSession => false,
        StaySignedInPolicy.AlwaysStaySignedIn => true,
        _ => personTicked
    };
}

/// <summary>
/// Who decides whether a sign-in outlives the browser: the values of
/// <see cref="IdentityProviderSettings.StaySignedInMode"/>.
/// </summary>
public enum StaySignedInPolicy
{
    /// <summary>
    /// The person: the sign-in page shows "Stay signed in", and a sign-in is a
    /// browser session unless they tick it.
    /// </summary>
    PersonChooses = 0,

    /// <summary>
    /// The administrator: the box is hidden and every sign-in is a browser
    /// session, for deployments whose computers several people share.
    /// </summary>
    AlwaysBrowserSession = 1,

    /// <summary>
    /// The administrator: the box is hidden and every sign-in lasts
    /// <see cref="IdentityProviderSettings.IdpSessionLifetimeDays"/> and survives
    /// closing the browser. Not for computers several people share.
    /// </summary>
    AlwaysStaySignedIn = 2
}
