using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>What a start's one message is, seen only by the mailbox's holder.</summary>
public enum EmailCodeStartMessageKind
{
    /// <summary>The code, to an address that has an account (one waiting for deletion included).</summary>
    SignInCode,

    /// <summary>The code, to a free address while sign-up is open.</summary>
    RegistrationCode,

    /// <summary>A notice, no code: the address is reserved by a deletion tombstone.</summary>
    ReservedNotice,

    /// <summary>A notice, no code: the address has no account and sign-up is closed.</summary>
    NoAccountNotice
}

/// <summary>The message a start sends, and what kind it is.</summary>
public sealed record EmailCodeStartMessage(EmailCodeStartMessageKind Kind, NotificationRequest Request)
{
    /// <summary>Whether the message carries the code the start minted.</summary>
    public bool CarriesCode => Kind is EmailCodeStartMessageKind.SignInCode or EmailCodeStartMessageKind.RegistrationCode;
}

/// <summary>
/// The one classification of an address behind both starts that mint a code for it
/// (OI-215, REVIEW 110 F1): the sign-in page's start and the registration's. An address
/// that has an account always receives the sign-in code, whichever start minted it, so
/// the code in a mailbox is always one its holder can use, and a start of either kind
/// never mints a code that opens an account without sending it to that account.
/// </summary>
/// <remarks>
/// Decided after both lookups and the row, from server state only, and never visible
/// in the answer: the starts answer every address alike, and only the message differs.
/// </remarks>
public class EmailCodeStartMessages
{
    private readonly EmailSettings _emailSettings;

    public EmailCodeStartMessages(IOptionsSnapshot<EmailSettings> emailSettings)
    {
        _emailSettings = emailSettings.Value;
    }

    /// <summary>The one message a start sends for <paramref name="email"/>.</summary>
    /// <param name="owner">The account the address belongs to, or null.</param>
    /// <param name="reserved">Whether a deletion tombstone reserves the address.</param>
    /// <param name="selfRegistrationOpen">Whether a free address may sign up.</param>
    /// <param name="requestLanguage">The language of the request, for an address with no stored one.</param>
    /// <param name="code">The code the start minted.</param>
    public EmailCodeStartMessage Choose(
        string email,
        UserNotificationIdentity? owner,
        bool reserved,
        bool selfRegistrationOpen,
        string requestLanguage,
        string code)
    {
        if (owner is not null)
        {
            return new EmailCodeStartMessage(
                EmailCodeStartMessageKind.SignInCode, SignInCodeMessage(email, owner, requestLanguage, code));
        }

        if (reserved)
        {
            return new EmailCodeStartMessage(
                EmailCodeStartMessageKind.ReservedNotice, ReservedNoticeMessage(email, requestLanguage));
        }

        return selfRegistrationOpen
            ? new EmailCodeStartMessage(
                EmailCodeStartMessageKind.RegistrationCode, RegistrationCodeMessage(email, requestLanguage, code))
            : new EmailCodeStartMessage(
                EmailCodeStartMessageKind.NoAccountNotice, NoAccountNoticeMessage(email, requestLanguage));
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
        // No user and no name: there is none yet. The language is stated
        // explicitly so the renderer looks nobody up.
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
        // A reserved address has no owner and no stored language: the request's.
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
}
