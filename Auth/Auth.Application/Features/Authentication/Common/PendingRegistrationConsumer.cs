using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Shared service that consumes an address's pending verify-first registration
/// once an account exists for it, whichever door created the account. Sits
/// beside <see cref="PersonalOrganizationCreator"/>, and like it is
/// best-effort: the account is already written when this runs.
/// </summary>
public class PendingRegistrationConsumer : IPendingRegistrationConsumer
{
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly ILogger<PendingRegistrationConsumer> _logger;

    public PendingRegistrationConsumer(
        IPendingRegistrationRepository pendingRegistrations,
        ILogger<PendingRegistrationConsumer> logger)
    {
        _pendingRegistrations = pendingRegistrations;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ConsumeAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            // The key the pending row is stored under — one derivation, the
            // entity's, for every reader.
            var consumed = await _pendingRegistrations.ConsumeByEmailAsync(
                PendingRegistration.NormalizeKey(email), cancellationToken);

            if (consumed > 0)
            {
                _logger.LogInformation(
                    "Pending registration for {Email} consumed: an account now exists for the address",
                    EmailMasking.Mask(email));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The account exists and the unique index on Users already keeps
            // a second one out; a stale pending row is hygiene, not safety.
            _logger.LogError(ex,
                "Pending registration for {Email} could not be consumed after the account was created",
                EmailMasking.Mask(email));
        }
    }
}
