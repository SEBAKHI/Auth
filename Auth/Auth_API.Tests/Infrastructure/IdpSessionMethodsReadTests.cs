using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// S23, D-S23-2 (a): the authorize endpoint and the code exchange decide an application's
/// two-step requirement from what the SSO session proved, read back from
/// <c>IdpSessions.AuthMethods</c>. Before S23 no read selected the column, so every session
/// read as Unknown. Every read selects it and maps it; NULL stays Unknown.
/// </summary>
/// <remarks>Deliberate break: drop <c>[AuthMethods]</c> from one SELECT; that read turns red.</remarks>
public class IdpSessionMethodsReadTests
{
    private static readonly AuthenticationMethods PasswordAndTotp =
        AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private static object Row(int? authMethods) => new
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TokenHash = "t", ExpiresAt = DateTime.UtcNow.AddDays(1),
        RevokedAt = (DateTime?)null, CreatedAt = DateTime.UtcNow, IpAddress = "198.51.100.23",
        DeviceInfo = "agent", IsPersistent = (bool?)false, LastActivityAt = (DateTime?)null,
        DeviceId = (string?)null, AuthMethods = authMethods
    };

    public static TheoryData<string> Reads => ["by token hash", "including revoked", "by id"];

    private static Task<IdpSession?> Read(string name, IdpSessionRepository repository) => name switch
    {
        "by token hash" => repository.GetByTokenHashAsync(
            FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "t"), CancellationToken.None),
        "including revoked" => repository.GetByTokenHashIncludingRevokedAsync(
            FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "t"), CancellationToken.None),
        _ => repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None),
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EveryRead_SelectsAndMapsWhatTheSessionProved(string name)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => Row(PasswordAndTotp.Value));

        var session = await Read(name, new IdpSessionRepository(db));

        db.LastCommand!.CommandText.Should().Contain("[AuthMethods]", name);
        session!.Methods.Should().Be(PasswordAndTotp, name);
        session.Methods.IsMfaSatisfied.Should().BeTrue(name);
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task ANullColumn_ReadsUnknown(string name)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => Row(null));

        var session = await Read(name, new IdpSessionRepository(db));

        session!.Methods.Should().Be(AuthenticationMethods.Unknown, name);
    }
}
