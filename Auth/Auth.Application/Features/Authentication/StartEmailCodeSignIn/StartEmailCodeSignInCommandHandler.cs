using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.StartRegistration;
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

namespace Auth.Application.Features.Authentication.StartEmailCodeSignIn;

/// <summary>
/// Starts a sign-in by emailed code (OI-215): one request, the same answer for every
/// address, and one message whose kind only the mailbox's holder ever sees.
/// </summary>
/// <remarks>
/// <para>
/// The enumeration rule of verify-first registration, unchanged: the two lookups run
/// for every address in the same order, the same pending row is written (or left
/// alone) under the same lock, one message is rendered and enqueued, the same audit
/// event is raised, and the answer has the same shape. What differs is the message:
/// a sign-in code to an address with an account (an account waiting for deletion
/// included), the registration code to a free address while sign-up is open, a "no
/// account here" notice to a free address while it is closed, and today's notice to
/// an address a deletion tombstone reserves. The code is minted and stored in every
/// case; for the two notices it is never sent.
/// </para>
/// <para>
/// The row is the registration's own (<see cref="IPendingRegistrationRepository.StartAsync"/>):
/// one live row and one mail window per address, shared with the registration's start,
/// so the two doors together never mail an address more than either would alone.
/// </para>
/// <para>
/// Two refusals come before the address is classified, both the same for every
/// address: email sending off (nothing could deliver a code), and the validator's.
/// Unlike the registration's start, a closed sign-up is not one of them: an existing
/// account must still be able to sign in.
/// </para>
/// </remarks>
public class StartEmailCodeSignInCommandHandler
    : IRequestHandler<StartEmailCodeSignInCommand, ErrorOr<StartRegistrationResponse>>
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
    private readonly ILogger<StartEmailCodeSignInCommandHandler> _logger;

    public StartEmailCodeSignInCommandHandler(
        IPendingRegistrationRepository pendingRegistrations,
        IPendingRegistrationHandle handle,
        IUserRepository userRepository,
        IdentifierReservationGuard reservationGuard,
        INotificationService notificationService,
        IDomainEventDispatcher eventDispatcher,
        IOptionsSnapshot<RegistrationSettings> registrationSettings,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<StartEmailCodeSignInCommandHandler> logger)
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
        StartEmailCodeSignInCommand request,
        CancellationToken cancellationToken)
    {
        // Before the address is so much as normalized: with email off the same
        // refusal for every address, or the endpoint would classify addresses
        // while sending nothing.
        if (!_emailSettings.Enabled)
        {
            _logger.LogInformation(
                "Sign-in code refused for {Email}: email sending is off",
                EmailMasking.Mask(request.Email));
            return UserErrors.EmailCodeSignInUnavailable;
        }

        // One normalization, as in the registration's start: the mask, the key and
        // the recipient all derive from it.
        var email = request.Email.Trim().ToLowerInvariant();
        var maskedEmail = EmailMasking.Mask(email);
        var requestLanguage = Languages.Normalize(request.PreferredLanguage)
            ?? Languages.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            ?? Languages.Default;

        // Classification. Both lookups, always, in this order: a branch that skipped
        // the second would answer some addresses sooner than others.
        var reserved = await _reservationGuard.IsReservedAsync(email, cancellationToken);
        var owner = await _userRepository.GetNotificationIdentityByEmailAsync(email, cancellationToken);

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

        // A message only when a code was minted: a live code is already in the
        // mailbox, and a spent mail window is the guessing bound. Every kind is
        // rendered and enqueued the same way, and the row is stamped the same way.
        if (start.Action == PendingRegistrationStartAction.Minted)
        {
            var message = ChooseMessage(email, owner, reserved, requestLanguage, start.Code!, out var carriesCode);

            if (carriesCode)
            {
                LogCodeInDevelopment(email, start.Code!);
            }

            var sent = await _notificationService.SendAsync(message, cancellationToken);

            // A failure changes nothing the caller sees: MailedAt stays null, so the
            // next start for this address mints again.
            if (sent.IsError)
            {
                _logger.LogError(
                    "Sign-in code message for {Email} could not be enqueued: {Error}",
                    maskedEmail, sent.FirstError.Description);
            }
            else
            {
                await _pendingRegistrations.MarkMailedAsync(row.Id, row.OtpHash, cancellationToken);
            }
        }

        // The audit trail, for every start: the one the registration's start writes,
        // which carries the caller and never the classification.
        row.RecordStart(request.IpAddress, request.UserAgent);
        try
        {
            await _eventDispatcher.DispatchEventsAsync(row, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Sign-in code start for {Email} was not audited", maskedEmail);
        }

        return new StartRegistrationResponse(row.Handle, maskedEmail, row.ExpiresAt);
    }

    /// <summary>
    /// The one message this start sends, by what the address is. Decided here and
    /// nowhere else, after both lookups and the row.
    /// </summary>
    private NotificationRequest ChooseMessage(
        string email,
        UserNotificationIdentity? owner,
        bool reserved,
        string requestLanguage,
        string code,
        out bool carriesCode)
    {
        if (owner is not null)
        {
            carriesCode = true;
            return SignInCodeMessage(email, owner, requestLanguage, code);
        }

        if (reserved)
        {
            carriesCode = false;
            return ReservedNoticeMessage(email, requestLanguage);
        }

        if (_registrationSettings.AllowSelfRegistration)
        {
            carriesCode = true;
            return RegistrationCodeMessage(email, requestLanguage, code);
        }

        carriesCode = false;
        return NoAccountNoticeMessage(email, requestLanguage);
    }

    private NotificationRequest SignInCodeMessage(
        string email, UserNotificationIdentity owner, string requestLanguage, string code)
    {
        var name = User.NameToShowFrom(owner.DisplayName, email);
        return new NotificationRequest
        {
            TypeCode = NotificationTypeCodes.EmailSignInCode,
            RecipientAddress = email,
            RecipientUserId = owner.Id,
            RecipientName = name,
            // The owner's stored language, stated explicitly so the renderer takes
            // no branch of its own.
            LanguageCode = Languages.Normalize(owner.PreferredLanguage) ?? requestLanguage,
            ApplicationId = null,
            TriggeredBy = Guid.Empty,
            Variables = new Dictionary<string, object?>
            {
                ["UserName"] = name,
                ["OtpCode"] = code,
                ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes
            }
        };
    }

    private NotificationRequest RegistrationCodeMessage(string email, string language, string code) => new()
    {
        TypeCode = NotificationTypeCodes.RegistrationVerification,
        RecipientAddress = email,
        // No user and no name: there is none yet.
        LanguageCode = language,
        TriggeredBy = Guid.Empty,
        Variables = new Dictionary<string, object?>
        {
            ["OtpCode"] = code,
            ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes
        }
    };

    private NotificationRequest ReservedNoticeMessage(string email, string language) => new()
    {
        TypeCode = NotificationTypeCodes.RegistrationAttemptExistingAccount,
        RecipientAddress = email,
        LanguageCode = language,
        TriggeredBy = Guid.Empty,
        Variables = new Dictionary<string, object?>
        {
            ["SignInLink"] = _emailSettings.BuildFrontendUrl("/login"),
            ["ResetPasswordLink"] = _emailSettings.BuildFrontendUrl("/forgot-password"),
            ["AttemptedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        }
    };

    private NotificationRequest NoAccountNoticeMessage(string email, string language) => new()
    {
        TypeCode = NotificationTypeCodes.SignInNoAccount,
        RecipientAddress = email,
        LanguageCode = language,
        TriggeredBy = Guid.Empty,
        Variables = new Dictionary<string, object?>
        {
            ["SignInLink"] = _emailSettings.BuildFrontendUrl("/login"),
            ["AttemptedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        }
    };

    /// <summary>
    /// The code in the development log, for a message that carries one: this start
    /// runs only while email is on, and a development machine rarely has a mail
    /// server, so the log is where a developer reads it. Gated on the environment:
    /// the code is the whole proof of the mailbox, and must never reach a production
    /// log. Never for a notice, whose code is never sent.
    /// </summary>
    private void LogCodeInDevelopment(string email, string code)
    {
        if (_environment.IsDevelopment)
        {
            _logger.LogWarning(
                "Development only - sign-in code for {Email}: {Otp} (expires in {Minutes} minutes)",
                EmailMasking.Mask(email), code, _emailSettings.OtpExpirationMinutes);
        }
    }
}
