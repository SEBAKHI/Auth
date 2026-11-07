using Auth.Application.DTOs;
using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationMfaReadiness;

/// <summary>
/// Counts the users who started a session in the application in the last
/// <see cref="WindowDays"/> days, and how many of them hold a qualifying second factor, by
/// the same definition the requirement reads: the usable factors (S20, D-150-4,
/// <see cref="UsableSecondFactors"/>), so a passkey counts only while passkeys run, exactly when
/// the requirement would accept one. One read; it changes nothing.
/// </summary>
public class GetApplicationMfaReadinessQueryHandler
    : IRequestHandler<GetApplicationMfaReadinessQuery, ErrorOr<ApplicationMfaReadinessDto>>
{
    /// <summary>The window of recent use: users who have not signed in for longer are not counted.</summary>
    public const int WindowDays = 30;

    private readonly IApplicationRepository _applicationRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly UsableSecondFactors _usableFactors;
    private readonly TimeProvider _timeProvider;

    public GetApplicationMfaReadinessQueryHandler(
        IApplicationRepository applicationRepository,
        ITwoFactorStateStore twoFactorStateStore,
        UsableSecondFactors usableFactors,
        TimeProvider timeProvider)
    {
        _applicationRepository = applicationRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _usableFactors = usableFactors;
        _timeProvider = timeProvider;
    }

    public async Task<ErrorOr<ApplicationMfaReadinessDto>> Handle(
        GetApplicationMfaReadinessQuery request, CancellationToken cancellationToken)
    {
        if (await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken) is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var since = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-WindowDays);
        var readiness = await _twoFactorStateStore.GetApplicationMfaReadinessAsync(
            request.ApplicationId, since, await _usableFactors.PasskeysCountAsync(cancellationToken), cancellationToken);

        return new ApplicationMfaReadinessDto
        {
            ActiveUsers = readiness.ActiveUsers,
            WithQualifyingFactor = readiness.WithQualifyingFactor,
            WithoutQualifyingFactor = readiness.WithoutQualifyingFactor,
            WindowDays = WindowDays
        };
    }
}
