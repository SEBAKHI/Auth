using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
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
/// out, chosen by <see cref="EmailCodeStartMessages"/>, the classification the
/// sign-in page's start uses too (OI-215, REVIEW 110 F1): the registration code
/// to a free address; the sign-in code to an address that has an account, since
/// that code also signs in to the account and must never be minted for it
/// without reaching it; and a notice, with the code never sent, to an address a
/// deletion tombstone reserves.
/// </para>
/// <para>
/// Two refusals come before anything else, both the same for every address: a
/// server that does not allow self-registration, and email sending off (REVIEW-FIXES
/// 110 G1). Neither may classify the address, or the refusal becomes an oracle for
/// who is registered here. With email off nothing could deliver a code, so none is
/// minted: a code for an address with an account would otherwise sit live and
/// "mailed" in its row, and the owner's next start would leave it alone and send
/// nothing once email came back on.
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
    private readonly EmailCodeStartMessages _messages;
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
        EmailCodeStartMessages messages,
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
        _messages = messages;
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

        // Email sending off, at the same position and for the same reason: one
        // refusal for every address, the sign-in page's own (its start, and every
        // step that takes a code, refuse the same way while email is off).
        if (!_emailSettings.Enabled)
        {
            _logger.LogInformation(
                "Self-registration start refused for {Email}: email sending is off",
                EmailMasking.Mask(request.Email));
            return UserErrors.EmailCodeSignInUnavailable;
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
        // someone's inbox, and a spent mail window is the guessing bound. Every
        // kind is rendered and enqueued the same way, and the row is stamped the
        // same way, so they cost the same on the wire and in the outbox. The door
        // above is open, so a free address always gets the registration code.
        if (start.Action == PendingRegistrationStartAction.Minted)
        {
            var message = _messages.Choose(
                email, owner, reserved, selfRegistrationOpen: true, requestLanguage, start.Code!);

            if (message.CarriesCode)
            {
                LogCodeInDevelopment(email, start.Code!);
            }

            var sent = await _notificationService.SendAsync(message.Request, cancellationToken);

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

    /// <summary>
    /// The code in the development log, for a message that carries one, as the
    /// sign-in page's start does: this start runs only while email is on, which is
    /// now the only way to sign up on a development machine, and such a machine
    /// rarely has a mail server, so the log is where a developer reads it. Gated on
    /// the environment: the code is the whole proof of the mailbox — presenting it
    /// turns a stranger's typing into an account, or signs in to one — and must
    /// never reach a production log. Never for the notice to a reserved address,
    /// whose code is never sent.
    /// </summary>
    private void LogCodeInDevelopment(string email, string code)
    {
        if (_environment.IsDevelopment)
        {
            _logger.LogWarning(
                "Development only - OTP for {Email}: {Otp} (expires in {Minutes} minutes)",
                EmailMasking.Mask(email), code, _emailSettings.OtpExpirationMinutes);
        }
    }
}
