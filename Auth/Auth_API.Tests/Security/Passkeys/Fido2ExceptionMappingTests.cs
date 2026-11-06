using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Infrastructure.Authentication.Passkeys;
using Auth_API.Tests.Helpers;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Security.Passkeys;

/// <summary>
/// S20 (PK10, ARR-0668): what the WebAuthn library raises for the client's input is mapped to
/// <c>Passkey.VerificationFailed</c> — the spike's list, plus <c>KeyNotFoundException</c>,
/// <c>ArgumentException</c> and <c>InvalidOperationException</c> when the library raised them —
/// and anything else propagates, which the error contract answers 500 <c>Http.Unexpected</c>
/// (<c>ErrorPipelineTests.Endpoint_WithProgrammingError_Returns500WithUnexpected</c> pins that
/// answer for these very types). The challenge is spent by then: the handler consumes it first
/// (<c>RegisterPasskeyCommandHandlerTests</c>).
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>KeyNotFoundException</c> removed from the list (a
/// library-raised one then escapes, and is answered <c>Http.Unexpected</c>); the "raised by the
/// library" condition dropped (this code's own one then maps); the stored options parsed inside
/// the mapped call.
/// </remarks>
public class Fido2ExceptionMappingTests
{
    private const string RpId = "localhost";
    private const string Origin = "https://localhost:5173";

    private static readonly PasskeyRelyingParty RelyingParty =
        new(RpId, RpId, new HashSet<string>(StringComparer.Ordinal) { Origin });

    private static readonly byte[] UserHandle = Enumerable.Range(1, 64).Select(i => (byte)i).ToArray();

    private readonly Fido2WebAuthnVerifier _verifier = new(NullLogger<Fido2WebAuthnVerifier>.Instance);

    /// <summary>
    /// Attestation objects that are CBOR but not of the shape a registration needs, beyond the
    /// spike's fields: the library raised its own exception for each (recorded 2026-11-05,
    /// Fido2 4.3.2), and the adapter maps every one.
    /// </summary>
    public static TheoryData<string, string> MalformedAttestationObjects => new()
    {
        { "an empty map", "A0" },
        { "fmt alone", "A163666D74646E6F6E65" },
        { "fmt and attStmt, no authData", "A263666D74646E6F6E656761747453746D74A0" },
        { "an empty authData", "A363666D74646E6F6E656761747453746D74A068617574684461746140" },
        { "a one-byte authData", "A363666D74646E6F6E656761747453746D74A06861757468446174614100" },
        { "an attStmt that is an array", "A363666D74646E6F6E656761747453746D74806861757468446174614100" },
        { "a fmt that is a number", "A163666D7401" },
        { "an array", "80" },
    };

    [Theory]
    [MemberData(nameof(MalformedAttestationObjects))]
    public async Task AMalformedAttestationObject_IsVerificationFailed(string because, string cbor)
    {
        var options = Options();
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var response = authenticator.Register(options, Origin,
            new CeremonyTweaks { RawAttestationObject = SoftwareAuthenticator.Base64Url(Convert.FromHexString(cbor)) });

        var result = await _verifier.VerifyRegistrationAsync(RelyingParty, options, response, CancellationToken.None);

        result.IsError.Should().BeTrue(because);
        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, because);
    }

    [Theory]
    [InlineData("A0")]
    [InlineData("A10102")]
    [InlineData("A201020326")]
    [InlineData("A10109")]
    public async Task AMalformedCosePublicKey_InARegistration_IsVerificationFailed(string cose)
    {
        var options = Options();
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var response = authenticator.Register(options, Origin, new CeremonyTweaks { CosePublicKey = Convert.FromHexString(cose) });

        (await _verifier.VerifyRegistrationAsync(RelyingParty, options, response, CancellationToken.None))
            .FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
    }

    /// <summary>
    /// PK4: the ceremony's origin must be one of the exact first-party origins; a sibling port
    /// or the bare parent host under the same RP ID is refused like any failed check.
    /// </summary>
    [Theory]
    [InlineData("https://localhost:5175")]
    [InlineData("https://localhost")]
    [InlineData("http://localhost:5173")]
    public async Task ARegistrationFromAnOriginNotListed_IsVerificationFailed(string origin)
    {
        var options = Options();
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var response = authenticator.Register(options, origin);

        (await _verifier.VerifyRegistrationAsync(RelyingParty, options, response, CancellationToken.None))
            .FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, origin);
    }

    [Fact]
    public async Task ARegistrationFromTheListedOrigin_Verifies()
    {
        var options = Options();
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);

        (await _verifier.VerifyRegistrationAsync(RelyingParty, options, authenticator.Register(options, Origin), CancellationToken.None))
            .IsError.Should().BeFalse("the control case: the same ceremony from the listed origin");
    }

    [Fact]
    public void AKeyNotFoundException_TheLibraryRaised_IsTheClients()
    {
        // The library parsing a COSE key without its parameters (what an assertion does).
        var raised = Catch(() => new CredentialPublicKey([0xA1, 0x01, 0x02]));

        raised.Should().BeOfType<KeyNotFoundException>("the recorded behaviour of Fido2 4.3.2");
        Fido2Failures.IsClientFault(raised, CancellationToken.None).Should().BeTrue();
    }

    public static TheoryData<Exception> ThrownByThisCode => new()
    {
        new KeyNotFoundException("ours"),
        new ArgumentException("ours"),
        new ArgumentNullException("ours"),
        new InvalidOperationException("ours"),
    };

    [Theory]
    [MemberData(nameof(ThrownByThisCode))]
    public void TheSameTypes_RaisedByThisCode_AreAFaultOfTheServer(Exception exception)
    {
        var thrown = Catch(() => throw exception);

        Fido2Failures.IsClientFault(thrown, CancellationToken.None).Should().BeFalse(
            "not from the library: a 500 answers it, never Passkey.VerificationFailed");
    }

    public static TheoryData<Exception> NeverTheClients => new()
    {
        new IOException("disk"),
        new NullReferenceException(),
        new InvalidCastException(),
        new HttpRequestException("network"),
        new OperationCanceledException(),
    };

    [Theory]
    [MemberData(nameof(NeverTheClients))]
    public void AnyOtherType_IsAFaultOfTheServer(Exception exception)
    {
        Fido2Failures.IsClientFault(Catch(() => throw exception), CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void TheCallersOwnCancellation_IsNeverTheClients()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Fido2Failures.IsClientFault(new OperationCanceledException(cancelled.Token), cancelled.Token).Should().BeFalse();
        Fido2Failures.IsClientFault(new TaskCanceledException(), cancelled.Token).Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(Fido2VerificationException))]
    [InlineData(typeof(JsonException))]
    public void TheSpikesTypes_AreTheClients_WhereverTheyComeFrom(Type type)
    {
        var exception = (Exception)Activator.CreateInstance(type, "a response that does not verify")!;

        Fido2Failures.IsClientFault(Catch(() => throw exception), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void TheLists_AreTheSpikesTwo_AndTheThreeOfArr0668()
    {
        Fido2Failures.ClientFaultTypes.Should().Equal(typeof(Fido2VerificationException), typeof(JsonException));
        Fido2Failures.LibraryRaisedClientFaultTypes.Should().Equal(
            typeof(KeyNotFoundException), typeof(ArgumentException), typeof(InvalidOperationException));
    }

    [Fact]
    public async Task StoredOptionsThatCannotBeRead_AreAFaultOfTheServer()
    {
        // The options are the server's own row: never answered as the client's fault.
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var response = authenticator.Register(Options(), Origin);

        var verify = () => _verifier.VerifyRegistrationAsync(RelyingParty, "{ not json", response, CancellationToken.None);

        await verify.Should().ThrowAsync<JsonException>();
    }

    private string Options() =>
        _verifier.CreateRegistrationOptions(RelyingParty, new PasskeyRegistrationSubject(UserHandle, "someone@example.com", "Someone"), []);

    private static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("nothing was thrown");
    }
}
