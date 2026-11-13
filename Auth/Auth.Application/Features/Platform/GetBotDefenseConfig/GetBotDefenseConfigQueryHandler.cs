using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Platform.GetBotDefenseConfig;

/// <summary>
/// Answers <see cref="GetBotDefenseConfigQuery"/> from the settings in force. Reads nothing else
/// and writes nothing.
/// </summary>
public sealed class GetBotDefenseConfigQueryHandler : IRequestHandler<GetBotDefenseConfigQuery, ErrorOr<BotDefenseConfigDto>>
{
    private readonly IOptionsMonitor<BotDefenseSettings> _settings;
    private readonly IEnvironmentInfo _environment;

    public GetBotDefenseConfigQueryHandler(IOptionsMonitor<BotDefenseSettings> settings, IEnvironmentInfo environment)
    {
        _settings = settings;
        _environment = environment;
    }

    public Task<ErrorOr<BotDefenseConfigDto>> Handle(GetBotDefenseConfigQuery request, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var siteKey = settings.SiteKey?.Trim();

        var nothingToCheck =
            settings.Mode == BotDefenseMode.Off
            || string.IsNullOrEmpty(siteKey)
            || (_environment.IsProduction
                && (TurnstileTestingKeys.IsTestingKey(siteKey) || TurnstileTestingKeys.IsTestingKey(settings.TurnstileSecret)));

        // Nothing to check reads Off: the public answer never says "enforced, nothing checked"
        // (REVIEW 167 S7), and the pages then wait for nothing.
        ErrorOr<BotDefenseConfigDto> answer = nothingToCheck
            ? new BotDefenseConfigDto(BotDefenseMode.Off, null)
            : new BotDefenseConfigDto(settings.Mode, siteKey);
        return Task.FromResult(answer);
    }
}
