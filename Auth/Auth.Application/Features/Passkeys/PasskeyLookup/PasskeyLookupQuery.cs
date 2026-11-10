using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.PasskeyLookup;

/// <summary>
/// The sign-in page's one question after the address (OI-280, D-S20-10): does this address
/// have a live passkey here? True sends the person to the passkey screen; false, which an
/// unknown address and an address without a passkey both get, leaves the sign-in as it was.
/// </summary>
/// <remarks>
/// Anonymous, and it reads only: no row, audit line, event, notice or challenge comes of it.
/// The owner accepted what it tells a stranger (D-S20-10): whether an address holds a passkey,
/// at the rate its own limiter allows. Nothing else of the account changes the answer.
/// </remarks>
/// <param name="Email">The address the person typed.</param>
public record PasskeyLookupQuery(string Email) : IRequest<ErrorOr<bool>>
{
    // The address is personal data, and together with this answer a record of who holds a
    // passkey: no log line may carry it (PK16 and its address rule).
    public override string ToString() => "PasskeyLookupQuery { }";
}
