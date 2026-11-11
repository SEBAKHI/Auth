using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ExchangeGoogleCode;

/// <summary>
/// Handles <see cref="ExchangeGoogleCodeCommand"/>: one call to Google's token endpoint.
/// </summary>
/// <remarks>
/// No database, no event, no audit row: nothing here changes this system's state. The sign-in
/// that follows (<c>external-login</c>) writes its own audit row, as it did when Google's
/// button handed the app the token directly.
/// </remarks>
public class ExchangeGoogleCodeCommandHandler
    : IRequestHandler<ExchangeGoogleCodeCommand, ErrorOr<GoogleIdTokenResult>>
{
    private readonly IGoogleCodeExchanger _exchanger;

    public ExchangeGoogleCodeCommandHandler(IGoogleCodeExchanger exchanger)
    {
        _exchanger = exchanger;
    }

    public async Task<ErrorOr<GoogleIdTokenResult>> Handle(
        ExchangeGoogleCodeCommand request,
        CancellationToken cancellationToken)
    {
        var idToken = await _exchanger.ExchangeAsync(
            request.Code,
            request.CodeVerifier,
            request.RedirectUri,
            cancellationToken);

        return idToken.IsError ? idToken.Errors : new GoogleIdTokenResult(idToken.Value);
    }
}
