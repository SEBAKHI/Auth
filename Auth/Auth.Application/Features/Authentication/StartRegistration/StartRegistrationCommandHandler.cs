using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.StartRegistration;

/// <summary>
/// Starts a self-registration for an address without creating anything for it.
/// </summary>
/// <remarks>
/// <para>
/// The governing rule of verify-first registration: no Users row, no password
/// hash, no organization and no UserCreated audit row exist before the caller
/// presents a code that reached the address they typed. This handler therefore
/// depends on none of the things that create an account — no password hasher,
/// no organization creator, no user factory — and a test holds it to that.
/// </para>
/// <para>
/// The enumeration rule: the response and the work are the same for every
/// address. Whether it is free, already an account, soft-deleted, or reserved
/// by a deletion tombstone, the handler does the same two lookups in the same
/// order, writes (or rotates) the same pending row under the same lock, renders
/// and enqueues one message, stamps the row, raises the same audit event, and
/// answers with the same shape. The single difference is which message goes
/// out: a code to a free address, or a notice to the address's owner that the
/// address cannot be used for a new account. The code is minted and stored for
/// a taken address too — it is simply never sent — so the verify step has
/// nothing to branch on either.
/// </para>
/// <para>
/// The door is checked before anything else: a server that does not allow
/// self-registration must not classify the address, or the closed door becomes
/// an oracle for who is registered here.
/// </para>
/// </remarks>
public class StartRegistrationCommandHandler
    : IRequestHandler<StartRegistrationCommand, ErrorOr<StartRegistrationResponse>>
{
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly IPendingRegistrationHandle _handle;
    private readonly IUserRepository _userRepository;
    private readonly IdentifierReservationGuard _reservationGuard;
    private readonly INotificationService _notificationService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly RegistrationSettings _registrationSettings;
    private readonly EmailSettings _emailSettings;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<StartRegistrationCommandHandler> _logger;

    public StartRegistrationCommandHandler(
        IPendingRegistrationRepository pendingRegistrations,
        IPendingRegistrationHandle handle,
        IUserRepository userRepository,
        IdentifierReservationGuard reservationGuard,
        INotificationService notificationService,
        IDomainEventDispatcher eventDispatcher,
        IOptionsSnapshot<RegistrationSettings> registrationSettings,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<StartRegistrationCommandHandler> logger)
    {
        _pendingRegistrations = pendingRegistrations;
        _handle = handle;
        _userRepository = userRepository;
        _reservationGuard = reservationGuard;
        _notificationService = notificationService;
        _eventDispatcher = eventDispatcher;
        _registrationSettings = registrationSettings.Value;
        _emailSettings = emailSettings.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<ErrorOr<StartRegistrationResponse>> Handle(
        StartRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        // The door, before the address is so much as normalized. Ahead of every
        // lookup on purpose: when the door is shut every address must get the
        // same refusal, or the endpoint keeps working as an oracle for who is
        // registered here.
        if (!_registrationSettings.AllowSelfRegistration)
        {
            _logger.LogInformation(
                "Self-registration start refused for {Email} from a closed server",
                EmailMasking.Mask(request.Email));
            return UserErrors.SelfRegistrationClosed;
        }

        // One normalization, and everything below — the mask the screen shows,
        // the key the row is stored under, the address the message goes to —
        // derives from it. Masking the raw input in one branch and the stored
        // address in another would let the letter case of the reply say
        // whether a row was found.
        var email = request.Email.Trim().ToLowerInvariant();
        var maskedEmail = EmailMasking.Mask(email);
        var requestLanguage = Languages.Normalize(request.PreferredLanguage)
            ?? Languages.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            ?? Languages.Default;

        // Classification. Both lookups, always, in this order, whatever the
        // first one says: a branch that skipped the second would answer a
        // reserved address tens of milliseconds sooner than a free one, and
        // that difference is what a deletion tombstone is meant to hide.
        var reserved = await _reservationGuard.IsReservedAsync(email, cancellationToken);
        var owner = await _userRepository.GetNotificationIdentityByEmailAsync(email, cancellationToken);
        var addressIsFree = !reserved && owner is null;

        // The row, for every class of address. The repository holds the lock,
        // finds or creates the address's one unconsumed row, leaves a live and
        // mailed code alone, rotates a dead one, and charges the address's mail
        // window — the same statements for a free address and a taken one.
        var start = await _pendingRegistrations.StartAsync(
            new PendingRegistrationStartRequest(
                Handle: _handle.For(PendingRegistration.NormalizeKey(email)),
                Email: email,
                PreferredLanguage: requestLanguage,
                ExpirationMinutes: _emailSettings.OtpExpirationMinutes,
                MailWindow: _emailSettings.RateLimitWindow,
                MaxMailsPerWindow: _emailSettings.MaxOtpRequestsPerWindow),
            cancellationToken);
        var row = start.Row;

        // A message only when a code was minted: a live code is already in
        // someone's inbox, and a spent mail window is the guessing bound. The
        // code itself goes out only to a free address; its owner-facing twin
        // says the address cannot be used for a new account. Both are rendered
        // and enqueued the same way, and the row is stamped the same way, so
        // the two cost the same on the wire and in the outbox.
        if (start.Action == PendingRegistrationStartAction.Minted)
        {
            if (addressIsFree)
            {
                LogCodeInDevelopment(email, start.Code!);
            }

            var message = addressIsFree
                ? CodeMessage(email, requestLanguage, start.Code!)
                : NoticeMessage(email, owner, requestLanguage);

            var sent = await _notificationService.SendAsync(message, cancellationToken);

            // A failure here changes nothing the caller sees. The row is
            // committed and MailedAt stays null, so the next start for this
            // address mints again instead of trusting a message nobody got.
            if (sent.IsError)
            {
                _logger.LogError(
                    "Registration message for {Email} could not be enqueued: {Error}",
                    maskedEmail, sent.FirstError.Description);
            }
            else
            {
                await _pendingRegistrations.MarkMailedAsync(row.Id, row.OtpHash, cancellationToken);
            }
        }

        // The audit trail, for every start. Abandoned and abusive attempts leave
        // no Users row under verify-first registration, so this is the only row
        // they leave — and a failure to write it must not undo a start that has
        // already committed its row and sent its message.
        row.RecordStart(request.IpAddress, request.UserAgent);
        try
        {
            await _eventDispatcher.DispatchEventsAsync(row, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Registration start for {Email} was not audited", maskedEmail);
        }

        // The stored expiry in every branch. A nominal "now + N minutes" would
        // lie to the countdown whenever the row's live code was left alone.
        return new StartRegistrationResponse(row.Handle, maskedEmail, row.ExpiresAt);
    }

    private NotificationRequest CodeMessage(string email, string language, string code) => new()
    {
        TypeCode = NotificationTypeCodes.RegistrationVerification,
        RecipientAddress = email,
        // No RecipientUserId and no RecipientName: there is no user. The
        // language is stated explicitly so the renderer looks nobody up.
        LanguageCode = language,
        TriggeredBy = Guid.Empty,
        Variables = new Dictionary<string, object?>
        {
            ["OtpCode"] = code,
            ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes
        }
    };

    private NotificationRequest NoticeMessage(string email, UserNotificationIdentity? owner, string requestLanguage) => new()
    {
        TypeCode = NotificationTypeCodes.RegistrationAttemptExistingAccount,
        RecipientAddress = email,
        RecipientUserId = owner?.Id,
        // The delivery log's recipient column, like every other user-addressed
        // message; the template itself greets nobody by name.
        RecipientName = owner?.DisplayName,
        // The owner's stored language when there is an owner; a reserved
        // address has none and gets the language of the request that typed it.
        // Computed here, explicitly, so the renderer takes no branch of its own.
        LanguageCode = Languages.Normalize(owner?.PreferredLanguage) ?? requestLanguage,
        TriggeredBy = Guid.Empty,
        Variables = new Dictionary<string, object?>
        {
            ["SignInLink"] = _emailSettings.BuildFrontendUrl("/login"),
            ["ResetPasswordLink"] = _emailSettings.BuildFrontendUrl("/forgot-password"),
            ["AttemptedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        }
    };

    /// <summary>
    /// With email disabled the log is the only other place the code exists,
    /// which is what makes the flow testable locally. Gated on the environment
    /// as well as the setting: this code is the whole proof of the address —
    /// presenting it turns a stranger's typing into an account — and
    /// Email:Enabled is a hot setting an operator can flip from the console in
    /// production, so on its own it would put a live code in the production
    /// log. Only for a code that actually goes out: a code minted for a taken
    /// address is never sent, and must not be logged either.
    /// </summary>
    private void LogCodeInDevelopment(string email, string code)
    {
        if (!_emailSettings.Enabled && _environment.IsDevelopment)
        {
            _logger.LogWarning(
                "Email disabled - OTP for {Email}: {Otp} (expires in {Minutes} minutes)",
                EmailMasking.Mask(email), code, _emailSettings.OtpExpirationMinutes);
        }
    }
}
