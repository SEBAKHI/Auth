using System.Globalization;
using Auth.Application.Configuration;
using Auth.Application.Features.Users.ResetUserTwoFactor;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Tells the people who can act when an account's second factor reaches the
/// consecutive-failure limit and stops accepting codes (X12, LK8.5): the account's
/// owner, and the administrators who could reset the factor for it right now
/// (owner decision D-X12B-1 (a)).
/// </summary>
/// <remarks>
/// <para>
/// Whoever got that far passed the first sign-in step — the password, or a
/// sign-in provider linked to the account — so the owner learns that one of them is
/// in someone else's hands and resets the password at once, the first step out of
/// the lock. The administrators learn that the second step, their reset, will
/// be asked of them. One message per recipient, each rendered for its audience
/// (<see cref="Audiences"/>): the owner's never lists the administrators, and no
/// message carries an address, a device, a client address or a code.
/// </para>
/// <para>
/// The transition happens once per lock (the reservation that reaches the limit),
/// so this sends once per lock; the password's hard lock (the same event with
/// <see cref="HardLockedFactor.Password"/>) is not this handler's.
/// </para>
/// <para>
/// The owner's message goes first, before the administrators are looked up: it is
/// the urgent one, and a failed lookup must not cost it. An account waiting for
/// deletion is told nothing: neither step out of the lock reaches it (the password
/// reset and an administrator's reset both read live accounts only), so a message
/// naming them would be a trap. A deletion asked from the account's own signed-in
/// session recovers without the limit (owner decisions 2026-10-12, X12 PR B row
/// «pending deletion» (b), narrowed by D-66-1 (a)), so its factor never gets here;
/// one asked through the public no-login page recovers under the limit and can —
/// the residual of D-66-1 (a), logged at Warning.
/// </para>
/// <para>
/// Best-effort: the lock has already been written. A failure — a read, the
/// notification service's error, an exception from writing the outbox — is logged
/// and never propagated, because the event's handlers run one after another and an
/// exception here would skip the audit handler and turn the request's answer into
/// an error. A message that could not be sent is not retried.
/// </para>
/// </remarks>
public class TwoFactorHardLockAdminNotificationEventHandler : INotificationHandler<UserHardLockedEvent>
{
    /// <summary>The values of the template's <c>Audience</c> variable.</summary>
    public static class Audiences
    {
        /// <summary>The account's owner.</summary>
        public const string Owner = "owner";

        /// <summary>An administrator who could reset the factor.</summary>
        public const string Administrator = "administrator";
    }

    private readonly IUserRepository _userRepository;
    private readonly TwoFactorResetAdministrators _administrators;
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<TwoFactorHardLockAdminNotificationEventHandler> _logger;

    public TwoFactorHardLockAdminNotificationEventHandler(
        IUserRepository userRepository,
        TwoFactorResetAdministrators administrators,
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<TwoFactorHardLockAdminNotificationEventHandler> logger)
    {
        _userRepository = userRepository;
        _administrators = administrators;
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(UserHardLockedEvent notification, CancellationToken cancellationToken)
    {
        if (notification.Factor != HardLockedFactor.SecondFactor)
        {
            return;
        }

        try
        {
            // Read including an account waiting for deletion, so that case is said
            // in the log rather than read as an account that is gone.
            var account = await _userRepository.GetByIdIncludeDeletedAsync(notification.UserId, cancellationToken);
            if (account is null)
            {
                _logger.LogWarning(
                    "No notice for the hard-locked second factor of account {UserId}: the account is gone",
                    notification.UserId);
                return;
            }

            if (account.IsDeleted)
            {
                _logger.LogWarning(
                    "No notice for the hard-locked second factor of account {UserId}: the account is waiting for deletion, and neither the password reset nor an administrator's reset reaches it",
                    account.Id);
                return;
            }

            var accountName = account.NameToShow;

            // The same values for every audience: what one message can say, the
            // others could too. The template decides what each audience reads.
            var values = new Dictionary<string, object?>
            {
                ["AccountName"] = accountName,
                ["AccountId"] = account.Id.ToString(),
                ["AccountLink"] = _emailSettings.BuildFrontendUrl($"/users/{account.Id}"),
                ["FailedAttempts"] = notification.FailedAttempts,
                ["OccurredAtUtc"] = notification.OccurredAt.UtcDateTime.ToString("u", CultureInfo.InvariantCulture),
                ["ResetPasswordLink"] = _emailSettings.BuildFrontendUrl("/forgot-password"),
            };

            await SendAsync(
                account.Id, account.Email, accountName, Audiences.Owner, values, notification, cancellationToken);

            var administrators = await _administrators.FindAsync(account.Id, cancellationToken);

            // Nobody told means nobody can reset it right now. An administrator who
            // holds the permission but has no second factor of their own can still
            // set one up and then reset; the server owner's emergency script is the
            // exit only when no administrator holds the permission at all.
            _logger.Log(
                administrators.Count == 0 ? LogLevel.Warning : LogLevel.Information,
                "The hard-locked second factor of account {UserId} is reported to its owner and {AdministratorCount} administrators who can reset it",
                account.Id, administrators.Count);

            foreach (var administrator in administrators)
            {
                await SendAsync(
                    administrator.UserId, administrator.Email, User.NameToShowFrom(administrator.DisplayName, administrator.Email), Audiences.Administrator,
                    values, notification, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Failed to report the hard-locked second factor of account {UserId} (event {EventId})",
                notification.UserId, notification.EventId);
        }
    }

    private async Task SendAsync(
        Guid recipientId,
        string recipientAddress,
        string recipientName,
        string audience,
        IReadOnlyDictionary<string, object?> values,
        UserHardLockedEvent notification,
        CancellationToken cancellationToken)
    {
        // One recipient's failure does not cost the others their message.
        try
        {
            var variables = new Dictionary<string, object?>(values)
            {
                ["UserName"] = recipientName,
                ["Audience"] = audience,
            };

            var sendResult = await _notificationService.SendAsync(
                new NotificationRequest
                {
                    TypeCode = NotificationTypeCodes.TwoFactorHardLocked,
                    RecipientAddress = recipientAddress,
                    RecipientName = recipientName,
                    RecipientUserId = recipientId,
                    // The platform's own security notice: its global templates,
                    // whichever application the codes were entered from.
                    ApplicationId = null,
                    Variables = variables,
                },
                cancellationToken);

            if (sendResult.IsError)
            {
                _logger.LogError(
                    "Failed to send the two-factor-hard-locked notice ({Audience}) for account {UserId} to user {RecipientId}: {Error}",
                    audience, notification.UserId, recipientId, sendResult.FirstError.Description);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Failed to send the two-factor-hard-locked notice ({Audience}) for account {UserId} to user {RecipientId}",
                audience, notification.UserId, recipientId);
        }
    }
}
