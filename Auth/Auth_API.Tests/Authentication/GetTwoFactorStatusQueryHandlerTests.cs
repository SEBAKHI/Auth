using Auth.Application.Features.Authentication.GetTwoFactorStatus;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// AM-S08-1 (ARR-1060): the security page's count of recovery codes left — the
/// length of the stored array of hashes, which loses one entry per code spent;
/// null without an enabled factor.
/// </summary>
public class GetTwoFactorStatusQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ITwoFactorStateStore> _store = new();

    private GetTwoFactorStatusQueryHandler Handler() =>
        new(_store.Object, Mock.Of<ILogger<GetTwoFactorStatusQueryHandler>>());

    private void Given(TwoFactorSnapshot? snapshot) =>
        _store.Setup(s => s.GetSnapshotAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);

    private static TwoFactorSnapshot Enabled(string? codes) =>
        new(UserId, "v2:secret", codes, isEnabled: true, failedAttempts: 0, lockedUntil: null);

    [Fact]
    public async Task Count_IsTheLengthOfTheStoredSet_AndDropsByOneWhenACodeIsSpent()
    {
        Given(Enabled("[\"h1\",\"h2\",\"h3\",\"h4\",\"h5\",\"h6\",\"h7\",\"h8\",\"h9\",\"h10\"]"));
        (await Handler().Handle(new GetTwoFactorStatusQuery(UserId), CancellationToken.None))
            .Value.RecoveryCodesRemaining.Should().Be(10);

        // What a sign-in with one recovery code leaves stored (the proof's new set).
        Given(Enabled("[\"h1\",\"h2\",\"h3\",\"h4\",\"h5\",\"h6\",\"h7\",\"h8\",\"h9\"]"));
        (await Handler().Handle(new GetTwoFactorStatusQuery(UserId), CancellationToken.None))
            .Value.RecoveryCodesRemaining.Should().Be(9);
    }

    [Fact]
    public async Task NoFactor_OrAPendingOne_IsNull()
    {
        Given(null);
        (await Handler().Handle(new GetTwoFactorStatusQuery(UserId), CancellationToken.None))
            .Value.RecoveryCodesRemaining.Should().BeNull();

        Given(new TwoFactorSnapshot(UserId, "v2:secret", null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        (await Handler().Handle(new GetTwoFactorStatusQuery(UserId), CancellationToken.None))
            .Value.RecoveryCodesRemaining.Should().BeNull("a factor being set up has no codes that work");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task EnabledWithoutReadableCodes_IsZero(string? codes)
    {
        Given(Enabled(codes));

        (await Handler().Handle(new GetTwoFactorStatusQuery(UserId), CancellationToken.None))
            .Value.RecoveryCodesRemaining.Should().Be(0, "none works at sign-in either, so the page asks for new ones");
    }
}
