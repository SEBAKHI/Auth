using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using ErrorOr;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Authentication.Passkeys;

/// <summary>
/// <see cref="IWebAuthnVerifier"/> over Fido2 (fido2-net-lib, MIT), the only place the
/// library is used (PK1).
/// </summary>
/// <remarks>
/// <para>
/// The library is built per call, and only when a passkey request reaches it — which needs
/// <c>Passkeys:Enabled</c> on. No field of this class has a library type, so resolving it
/// loads nothing: if the library could not load on a host, the passkey requests alone
/// would fail, and the API would start and every other path would run (D-S20-3 (a)).
/// </para>
/// <para>
/// ES256 (-7) and RS256 (-257) only. The library's EdDSA path is the one that loads the
/// native libsodium (through NSec); it is never offered, and the library itself refuses a
/// credential whose algorithm the options did not offer
/// (<see cref="Fido2NetLib.Exceptions.Fido2ErrorCode.CredentialAlgorithmRequirementNotMet"/>).
/// </para>
/// <para>
/// What the client sent is checked by the library, and every rejection it raises is mapped
/// to <see cref="PasskeyErrors.VerificationFailed"/> by <see cref="Fido2Failures"/>: the
/// closed list of exception types the spike recorded for malformed or mismatched input
/// (<c>Fido2WebAuthnSpikeTests</c>). Any other exception propagates, and the error contract
/// answers 500 <c>Http.Unexpected</c>: a fault of the server is never reported as the
/// client's.
/// </para>
/// </remarks>
public sealed class Fido2WebAuthnVerifier : IWebAuthnVerifier
{
    /// <summary>The ceremony timeout offered to the browser (PK5): five minutes.</summary>
    public const uint TimeoutMilliseconds = 300_000;

    /// <summary>The size of the WebAuthn challenge, in bytes.</summary>
    public const int ChallengeBytes = 32;

    /// <summary>The algorithms offered and accepted (PK1): ES256, then RS256.</summary>
    public static readonly IReadOnlyList<int> Algorithms = [(int)COSE.Algorithm.ES256, (int)COSE.Algorithm.RS256];

    private readonly ILogger<Fido2WebAuthnVerifier> _logger;

    public Fido2WebAuthnVerifier(ILogger<Fido2WebAuthnVerifier> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string CreateRegistrationOptions(
        PasskeyRelyingParty relyingParty,
        PasskeyRegistrationSubject subject,
        IReadOnlyList<byte[]> excludeCredentialIds)
    {
        var fido2 = new Fido2(Configuration(relyingParty));

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = subject.UserHandle,
                Name = subject.Name,
                DisplayName = subject.DisplayName,
            },
            ExcludeCredentials = excludeCredentialIds
                .Select(id => new PublicKeyCredentialDescriptor(id))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
            PubKeyCredParams = Algorithms
                .Select(algorithm => new PubKeyCredParam((COSE.Algorithm)algorithm, PublicKeyCredentialType.PublicKey))
                .ToList(),
        });

        return options.ToJson();
    }

    /// <inheritdoc />
    public async Task<ErrorOr<VerifiedPasskeyCredential>> VerifyRegistrationAsync(
        PasskeyRelyingParty relyingParty,
        string optionsJson,
        string responseJson,
        CancellationToken cancellationToken)
    {
        // The server's own inputs, outside the mapped call: the options it stored and its
        // configuration. A failure in either is the server's, so it propagates.
        var options = CredentialCreateOptions.FromJson(optionsJson);
        var fido2 = new Fido2(Configuration(relyingParty));

        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(responseJson);
            if (response?.Response is null)
            {
                return Refused("the response has no attestation");
            }

            var credential = await fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = response,
                    OriginalOptions = options,
                    // Uniqueness is the unique index on UserPasskeys.CredentialId: a credential
                    // id already registered, to anyone, is refused when the row is written.
                    IsCredentialIdUniqueToUserCallback = static (_, _) => Task.FromResult(true),
                },
                cancellationToken);

            if (credential.Id is not { Length: > 0 and <= UserPasskey.MaxCredentialIdBytes })
            {
                return Refused("the credential id is empty or too long");
            }

            return new VerifiedPasskeyCredential(
                credential.Id,
                credential.PublicKey,
                credential.SignCount,
                credential.IsBackupEligible,
                credential.IsBackedUp);
        }
        catch (Exception ex) when (Fido2Failures.IsClientFault(ex, cancellationToken))
        {
            return Refused(Fido2Failures.Describe(ex));
        }
    }

    private Error Refused(string reason)
    {
        // The reason names the check, never a value the client sent (PK16): no credential
        // id, public key, user handle or client data reaches the log.
        _logger.LogInformation("Passkey registration refused: {Reason}", reason);
        return PasskeyErrors.VerificationFailed;
    }

    private static Fido2Configuration Configuration(PasskeyRelyingParty relyingParty) => new()
    {
        RPID = relyingParty.RpId,
        RPName = relyingParty.RpName,
        Origins = relyingParty.Origins.ToHashSet(StringComparer.Ordinal),
        ChallengeSize = ChallengeBytes,
        Timeout = TimeoutMilliseconds,
        // PK4: a ceremony inside a cross-origin frame is refused.
        AllowCrossOriginRequests = false,
    };
}

/// <summary>
/// The exception types the WebAuthn library raises for what the CLIENT sent, as the spike
/// recorded them one field at a time (<c>Fido2WebAuthnSpikeTests</c>: Fido2 4.3.2 raised only
/// the first two, malformed CBOR included), plus the three the plan names (ARR-0668): a
/// closed list, never <c>catch (Exception)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A type on the list is mapped to <see cref="PasskeyErrors.VerificationFailed"/>; any other
/// propagates as a server fault. A cancellation of the caller's own token is never a client
/// fault.
/// </para>
/// <para>
/// The three framework types of ARR-0668 are the client's only when the library itself raised
/// them ("from Fido2"): the first frame of the stack outside the framework is the library's.
/// Thrown by this code, or by a callback the library calls, they are a fault of the server.
/// The library raises one when it parses a COSE key directly — <c>KeyNotFoundException</c> for
/// a key without its parameters — which is what an assertion does with a STORED key (PR B):
/// that parse belongs outside the mapped call, so a stored key that cannot be read is a 500.
/// </para>
/// </remarks>
public static class Fido2Failures
{
    /// <summary>The types that are the client's wherever, in the mapped call, they come from.</summary>
    public static readonly IReadOnlyList<Type> ClientFaultTypes =
    [
        // Every check the library makes on a response: origin, challenge, RP ID hash, flags,
        // signature, algorithm, attestation statement, counter — and malformed CBOR.
        typeof(Fido2VerificationException),
        // The response, or a field of it, is not JSON of the expected shape (a member that is
        // not base64url, a credential type other than public-key).
        typeof(JsonException),
    ];

    /// <summary>
    /// ARR-0668: the types that are the client's only when the library raised them, on input
    /// it cannot read (a missing map key, a malformed field, a state the response does not
    /// allow).
    /// </summary>
    public static readonly IReadOnlyList<Type> LibraryRaisedClientFaultTypes =
    [
        typeof(KeyNotFoundException),
        typeof(ArgumentException),
        typeof(InvalidOperationException),
    ];

    private static readonly Assembly[] LibraryAssemblies =
    [
        typeof(Fido2).Assembly,
        typeof(AuthenticatorAttestationRawResponse).Assembly,
        typeof(Fido2VerificationException).Assembly,
    ];

    /// <summary>Whether <paramref name="exception"/> is a rejection of the client's input.</summary>
    public static bool IsClientFault(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var type = exception.GetType();
        if (ClientFaultTypes.Any(clientFault => clientFault.IsAssignableFrom(type)))
        {
            return true;
        }

        return LibraryRaisedClientFaultTypes.Any(clientFault => clientFault.IsAssignableFrom(type))
            && RaisedByTheLibrary(exception);
    }

    /// <summary>
    /// Whether the first frame of the exception's stack outside the framework belongs to the
    /// library: a dictionary or a CBOR reader the library called throws on its behalf.
    /// </summary>
    private static bool RaisedByTheLibrary(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames())
        {
            var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
            if (assembly is null || IsFramework(assembly))
            {
                continue;
            }

            return LibraryAssemblies.Contains(assembly);
        }

        return false;
    }

    private static bool IsFramework(Assembly assembly) =>
        assembly == typeof(object).Assembly
        || assembly.GetName().Name is { } name
            && (name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal));

    /// <summary>A log-safe reason: the exception type, and the library's error code when it has one.</summary>
    public static string Describe(Exception exception) =>
        exception is Fido2VerificationException verification
            ? $"{nameof(Fido2VerificationException)} {verification.Code}"
            : exception.GetType().Name;
}
