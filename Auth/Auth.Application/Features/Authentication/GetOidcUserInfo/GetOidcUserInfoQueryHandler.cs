using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetOidcUserInfo;

/// <summary>
/// Answers UserInfo from the user's row, read at the call, filtered by the token's scopes.
/// </summary>
/// <remarks>
/// The token proves who is asking and what they were granted; it is not the source of the
/// profile. Reading the row means a changed phone or name shows at the next call, and that a
/// deleted or locked account answers nothing at all, the same as a revoked token.
/// </remarks>
public class GetOidcUserInfoQueryHandler
    : IRequestHandler<GetOidcUserInfoQuery, ErrorOr<OidcUserInfoResponse>>
{
    /// <summary>
    /// The stored time zone that means "follow the browser" (the column's default). Not a zone
    /// a relying party can use, so it is not returned; <c>Etc/UTC</c> is the literal zone.
    /// </summary>
    private const string AutomaticTimeZone = "UTC";

    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetOidcUserInfoQueryHandler(IUserRepository userRepository, IImageUrlComposer imageUrlComposer)
    {
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<OidcUserInfoResponse>> Handle(
        GetOidcUserInfoQuery request,
        CancellationToken cancellationToken)
    {
        // Missing and soft-deleted rows are both null here.
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // The rule a refresh applies: an account that may not renew credentials is not served
        // by the credential it still holds either.
        if (!user.CanRenewCredentials())
        {
            return UserErrors.AccountLocked;
        }

        // OI-58's reading of a stored grant: no claim is openid only, an unknown word grants nothing.
        var granted = ScopeSet.FromStored(request.Scope);
        var profile = granted.Contains(OAuthScopes.Profile);
        var email = granted.Contains(OAuthScopes.Email);
        var phone = granted.Contains(OAuthScopes.Phone) ? Present(user.PhoneNumber?.Value) : null;

        return new OidcUserInfoResponse
        {
            Sub = user.Id.ToString(),
            Name = profile ? Present(user.GetFullName().Trim()) : null,
            GivenName = profile ? Present(user.FirstName) : null,
            FamilyName = profile ? Present(user.LastName) : null,
            Locale = profile ? Present(user.PreferredLanguage) : null,
            ZoneInfo = profile ? ZoneInfoOf(user) : null,
            Picture = profile ? PictureOf(user) : null,
            Email = email ? Present(user.Email.Value) : null,
            EmailVerified = email ? user.EmailConfirmed : null,
            PhoneNumber = phone,
            PhoneNumberVerified = phone is null ? null : user.PhoneConfirmed
        };
    }

    private static string? ZoneInfoOf(User user)
    {
        var zone = Present(user.TimeZone);
        return string.Equals(zone, AutomaticTimeZone, StringComparison.Ordinal) ? null : zone;
    }

    /// <summary>
    /// The picture as an absolute http(s) URL with no credentials in it, or nothing. A relative
    /// address (the default image base) means nothing to a relying party on another host.
    /// </summary>
    private string? PictureOf(User user) =>
        Uri.TryCreate(_imageUrlComposer.Compose(user.ProfileImageUrl), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.UserInfo.Length == 0
            ? uri.AbsoluteUri
            : null;

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
