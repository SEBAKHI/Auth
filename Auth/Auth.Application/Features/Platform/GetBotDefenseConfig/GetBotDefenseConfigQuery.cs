using Auth.Application.Configuration;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.GetBotDefenseConfig;

/// <summary>
/// What the sign-in pages need to offer the bot check (S13, BD8): the mode, and the widget's
/// public sitekey. Served anonymously, like the branding: the pages ask before anyone signs in.
/// </summary>
public sealed record GetBotDefenseConfigQuery : IRequest<ErrorOr<BotDefenseConfigDto>>;

/// <summary>The bot check as the pages see it. Public data only: never the secret.</summary>
/// <param name="Mode">Off, Monitor or Enforce. Under Enforce the pages wait for the token before sending.</param>
/// <param name="SiteKey">
/// The sitekey to render the widget with, or null where there is nothing to check: the mode is
/// Off, no sitekey is set, or a testing key is set in Production. With null, the pages load
/// nothing from Cloudflare.
/// </param>
public sealed record BotDefenseConfigDto(BotDefenseMode Mode, string? SiteKey);
