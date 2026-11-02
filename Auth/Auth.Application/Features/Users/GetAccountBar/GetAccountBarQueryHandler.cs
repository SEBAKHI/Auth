using Auth.Application.DTOs;
using Auth.Application.Features.Users.GetMyApplications;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetAccountBar;

/// <summary>
/// Handler for <see cref="GetAccountBarQuery"/>.
/// </summary>
/// <remarks>
/// The person is the one the IdP session cookie names, not the accounts app's bearer token:
/// the bar is drawn inside a linked application, where the person may never have opened the
/// accounts app, and a session of that app inside a frame would race the rotation of its
/// refresh token with the app's own tabs. It is also the session the application's next
/// sign-in will meet, so the bar shows whoever that sign-in would be.
/// <para>
/// The session is read in <see cref="IdpSessionReadMode.Inspect"/> mode: drawing the bar is
/// not using the session. A read that recorded a use would let any page of any linked
/// application keep an idle single sign-on session alive forever, just by being open.
/// </para>
/// </remarks>
public class GetAccountBarQueryHandler : IRequestHandler<GetAccountBarQuery, ErrorOr<AccountBarResult>>
{
    private readonly IIdpSessionReader _idpSessionReader;
    private readonly IUserRepository _userRepository;
    private readonly MyApplicationsList _applications;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetAccountBarQueryHandler(
        IIdpSessionReader idpSessionReader,
        IUserRepository userRepository,
        MyApplicationsList applications,
        IImageUrlComposer imageUrlComposer)
    {
        _idpSessionReader = idpSessionReader;
        _userRepository = userRepository;
        _applications = applications;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<AccountBarResult>> Handle(GetAccountBarQuery request, CancellationToken cancellationToken)
    {
        var session = await _idpSessionReader.FindLiveAsync(
            request.IdpSessionToken, IdpSessionReadMode.Inspect, cancellationToken);
        if (session is null)
        {
            return AccountBarResult.SignIn;
        }

        // The rule the authorize endpoint applies before it trusts the session: an account
        // that may no longer sign in (locked, disabled, deleted) is not shown as signed in.
        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.CanRenewCredentials())
        {
            return AccountBarResult.SignIn;
        }

        var applications = await _applications.ForUserAsync(user.Id, cancellationToken);

        return new AccountBarResult(false, new AccountBarDto
        {
            Name = user.NameToShow,
            Email = user.Email.Value,
            ProfileImageUrl = _imageUrlComposer.Compose(user.ProfileImageUrl),
            PreferredLanguage = user.PreferredLanguage,
            Theme = user.Theme,
            Applications = applications
        });
    }
}
