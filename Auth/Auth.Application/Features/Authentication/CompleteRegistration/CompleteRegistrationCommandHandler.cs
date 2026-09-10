using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.CompleteRegistration;

/// <summary>
/// Creates the account a verify-first registration was started for, once the
/// code has come back, and signs the new owner in.
/// </summary>
/// <remarks>
/// <para>
/// The order is the contract, and every step of it is placed for a reason:
/// </para>
/// <list type="number">
/// <item>The door. A closed server refuses before the code is so much as read.</item>
/// <item>The code's shape.</item>
/// <item>The code, under the row's lock. The one place an attempt is charged,
/// shared with the verify step, so five wrong codes across both close the
/// code. A wrong code is answered here — before any password work, before
/// any read of Users or the tombstones — so a caller with a handle and no
/// code spends nothing of ours and learns nothing about the address.</item>
/// <item>The password policy and the breach check. Outside any lock: the
/// breach check is an external call.</item>
/// <item>Whether the address already has an account or is reserved. After
/// the proof only: the same checks before it would let a code-less caller
/// classify addresses through this endpoint.</item>
/// <item>The hash, the entity, confirmed before it is ever written.</item>
/// <item>The account and the consumption in one transaction, with the code
/// checked once more under the row's lock and WITHOUT charging an attempt:
/// a mismatch there is the row having been rotated or consumed since step 3,
/// not a guess.</item>
/// </list>
/// <para>
/// After that commit nothing may fail the request. The account exists and the
/// pending row is consumed, so a 500 now would be a dead end for a person who
/// did everything right. Events and the optional organization degrade to log
/// lines; if the session itself cannot be issued the error is returned with a
/// log that the account exists, because the ordinary sign-in still works.
/// </para>
/// </remarks>
public class CompleteRegistrationCommandHandler : IRequestHandler<CompleteRegistrationCommand, ErrorOr<LoginResponse>>
{
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly IUserRepository _userRepository;
    private readonly IdentifierReservationGuard _reservationGuard;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly IPersonalOrganizationCreator _personalOrganizationCreator;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly RegistrationSettings _registrationSettings;
    private readonly ILogger<CompleteRegistrationCommandHandler> _logger;

    public CompleteRegistrationCommandHandler(
        IPendingRegistrationRepository pendingRegistrations,
        IUserRepository userRepository,
        IdentifierReservationGuard reservationGuard,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IPersonalOrganizationCreator personalOrganizationCreator,
        IDomainEventDispatcher eventDispatcher,
        ILoginResponseBuilder loginResponseBuilder,
        IOptionsSnapshot<RegistrationSettings> registrationSettings,
        ILogger<CompleteRegistrationCommandHandler> logger)
    {
        _pendingRegistrations = pendingRegistrations;
        _userRepository = userRepository;
        _reservationGuard = reservationGuard;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _personalOrganizationCreator = personalOrganizationCreator;
        _eventDispatcher = eventDispatcher;
        _loginResponseBuilder = loginResponseBuilder;
        _registrationSettings = registrationSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(CompleteRegistrationCommand request, CancellationToken cancellationToken)
    {
        // 1. The door.
        if (!_registrationSettings.AllowSelfRegistration)
        {
            _logger.LogInformation("Self-registration completion refused from a closed server");
            return UserErrors.SelfRegistrationClosed;
        }

        // 2. The shape.
        if (string.IsNullOrWhiteSpace(request.Otp) ||
            request.Otp.Length != 6 ||
            !request.Otp.All(char.IsDigit))
        {
            return EmailVerificationErrors.InvalidOtpFormat;
        }

        // 3. The code, under the lock. The only place an attempt is charged.
        var check = await _pendingRegistrations.CheckCodeUnderLockAsync(request.PendingId, request.Otp, cancellationToken);
        switch (check.Outcome)
        {
            case PendingRegistrationCodeOutcome.Match:
                break;
            case PendingRegistrationCodeOutcome.Exhausted:
                _logger.LogWarning("Registration completion refused: code for pending row {PendingRegistrationId} is exhausted", check.Row!.Id);
                return EmailVerificationErrors.TooManyAttempts;
            case PendingRegistrationCodeOutcome.Wrong:
                _logger.LogWarning("Registration completion refused: wrong code for pending row {PendingRegistrationId}", check.Row!.Id);
                return EmailVerificationErrors.InvalidOrExpiredOtp;
            default:
                return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        var row = check.Row!;
        var email = row.Email.Value;

        // 4. The password. Policy first, then the breach check (an external call,
        // and the reason none of this sits under a lock).
        var passwordValidation = _passwordValidator.Validate(request.Password);
        if (passwordValidation.IsError)
        {
            return passwordValidation.Errors;
        }

        var breachResult = await _breachEvaluator.EvaluateAsync(request.Password, cancellationToken);
        if (breachResult.IsError)
        {
            return breachResult.Errors;
        }

        // 5. The address, after the proof. Another door may have created the
        // account while the code was in transit; a tombstone may reserve it.
        // Both answer the ordinary conflict — the caller has proved the address,
        // so there is nothing left to hide from them.
        if (await _userRepository.ExistsByEmailAsync(email, cancellationToken))
        {
            return UserErrors.DuplicateEmail(email);
        }

        var reservation = await _reservationGuard.EnsureNotReservedAsync(email, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        // 6. The hash and the entity. Confirmed before it is written: the code
        // is the proof, so there is no window in which this account is recorded
        // as unconfirmed.
        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = User.Create(
            email: email,
            passwordHash: passwordHash,
            firstName: request.FirstName,
            lastName: request.LastName,
            createdBy: Guid.Empty,
            // The site language of the request that completes wins; the one
            // the code went out in is the fallback; then the default.
            preferredLanguage: Languages.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                ?? Languages.Normalize(row.PreferredLanguage)
                ?? Languages.Default,
            timeZone: request.TimeZone ?? "UTC");
        user.ConfirmEmail(user.Id);

        // 7. The account and the consumption, in one transaction.
        var creation = await _userRepository.CreateVerifiedAsync(user, row.Id, request.Otp, cancellationToken);
        switch (creation)
        {
            case VerifiedUserCreationOutcome.CodeRejected:
                // Rotated or consumed since step 3. Not a guess: nothing charged.
                _logger.LogWarning(
                    "Registration completion refused: pending row {PendingRegistrationId} changed under the lock",
                    row.Id);
                return EmailVerificationErrors.InvalidOrExpiredOtp;
            case VerifiedUserCreationOutcome.DuplicateEmail:
                return UserErrors.DuplicateEmail(email);
        }

        _logger.LogInformation(
            "User registered through verify-first registration: {UserId} ({Email})",
            user.Id, EmailMasking.Mask(email));

        // ── Past this line nothing may fail the request. ──

        // UserCreated (and whatever else the entity raised) — the audit row for
        // the account, written by a stranger.
        await DispatchBestEffortAsync(user, "creation", cancellationToken);

        if (request.CreateOrganization)
        {
            try
            {
                await _personalOrganizationCreator.CreateAsync(user, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Personal organization was not created for user {UserId}; the account exists", user.Id);
            }
        }

        // The session, exactly as a normal sign-in issues it. Whatever goes
        // wrong here — a refusal the builder can name, or a throw from the
        // token store — the account is confirmed and the ordinary sign-in
        // will issue what this could not, so the answer is one named error
        // the screen routes to "sign in", never a 500 and never a code error
        // that would send the person back to a code that is already spent.
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        ErrorOr<LoginResponse> loginResponse;
        try
        {
            loginResponse = await _loginResponseBuilder.BuildAsync(
                user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Account {UserId} was created and confirmed but its first session threw; the owner can sign in normally",
                user.Id);
            return UserErrors.AccountCreatedSignInRequired;
        }

        if (loginResponse.IsError)
        {
            _logger.LogWarning(
                "Account {UserId} was created and confirmed but its first session was refused: {Error}. The owner can sign in normally.",
                user.Id, loginResponse.FirstError.Description);
            return UserErrors.AccountCreatedSignInRequired;
        }

        await DispatchBestEffortAsync(user, "sign-in", cancellationToken);

        return loginResponse.Value;
    }

    private async Task DispatchBestEffortAsync(User user, string moment, CancellationToken cancellationToken)
    {
        try
        {
            await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Events for user {UserId} at {Moment} were not dispatched; the account exists", user.Id, moment);
        }
    }
}
