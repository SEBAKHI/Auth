using System.Collections.Frozen;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ClassifyTokenClient;

/// <summary>
/// Answers <see cref="ClassifyTokenClientQuery"/> with the application whose key verified the
/// request's client assertion, or <c>Auth.InvalidClient</c> when it does not verify.
/// </summary>
/// <remarks>
/// <para>
/// The verification is the grant's own (<see cref="IClientAuthenticator.VerifyAsync"/>, with the
/// purpose the grant requires), and the authenticator keeps its answer for the rest of the
/// request, so the grant does not verify a second time.
/// </para>
/// <para>
/// An assertion whose <c>jti</c> was already spent is not counted as its application: whoever
/// captured one valid assertion could otherwise replay it for its lifetime and spend that
/// application's budget, refused by the grant every time but counted first.
/// </para>
/// </remarks>
public sealed class ClassifyTokenClientQueryHandler : IRequestHandler<ClassifyTokenClientQuery, ErrorOr<Guid>>
{
    /// <summary>The key purpose each grant authenticates with (OI-171, D-171-2 (a)).</summary>
    private static readonly FrozenDictionary<string, MachineKeyPurpose> PurposeByGrant =
        new Dictionary<string, MachineKeyPurpose>(StringComparer.Ordinal)
        {
            ["client_credentials"] = MachineKeyPurpose.Machine,
            ["authorization_code"] = MachineKeyPurpose.SignIn,
            ["refresh_token"] = MachineKeyPurpose.SignIn,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly IClientAuthenticator _authenticator;
    private readonly IClientAssertionJtiStore _jtis;

    public ClassifyTokenClientQueryHandler(IClientAuthenticator authenticator, IClientAssertionJtiStore jtis)
    {
        _authenticator = authenticator;
        _jtis = jtis;
    }

    public async Task<ErrorOr<Guid>> Handle(ClassifyTokenClientQuery request, CancellationToken cancellationToken)
    {
        if (request.GrantType is null || !PurposeByGrant.TryGetValue(request.GrantType, out var purpose))
        {
            return AuthErrors.InvalidClient;
        }

        var verified = await _authenticator.VerifyAsync(
            new ClientAssertionRequest(request.AssertionType, request.Assertion, request.ClientId, request.IpAddress),
            purpose,
            cancellationToken);
        if (verified.IsError)
        {
            return verified.Errors;
        }

        var client = verified.Value;
        if (await _jtis.IsSpentAsync(client.ApplicationId, client.Jti, cancellationToken))
        {
            return AuthErrors.InvalidClient;
        }

        return client.ApplicationId;
    }
}
