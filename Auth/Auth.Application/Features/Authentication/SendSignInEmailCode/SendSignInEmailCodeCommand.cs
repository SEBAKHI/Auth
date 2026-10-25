using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SendSignInEmailCode;

/// <summary>
/// Command to email the code that completes a pending sign-in whose second factor is
/// the emailed code (S24). Anonymous: the challenge token is the only credential.
/// </summary>
/// <param name="ChallengeToken">The opaque challenge token the sign-in answered with.</param>
/// <param name="IpAddress">The caller's address, named in the email.</param>
public record SendSignInEmailCodeCommand(
    string ChallengeToken,
    string? IpAddress) : IRequest<ErrorOr<SignInEmailCodeResponse>>;

/// <summary>
/// Where the code went, until when it is accepted, and how many more the sign-in may ask for.
/// </summary>
/// <param name="ExpiresAt">When the code, and the pending sign-in, stop being accepted (UTC).</param>
/// <param name="MaskedEmail">The address the code went to, masked.</param>
/// <param name="SendsRemaining">How many more codes this sign-in may ask for; 0 hides the resend.</param>
public record SignInEmailCodeResponse(
    DateTime ExpiresAt,
    string MaskedEmail,
    int SendsRemaining)
{
    /// <summary>The answer for a code the sender emailed.</summary>
    public static SignInEmailCodeResponse From(EmailCodeSent sent) =>
        new(sent.ExpiresAt, sent.MaskedEmail, sent.SendsRemaining);
}
