using Auth.Application.Features.Users.UiPreferences;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using FluentValidation.TestHelper;

namespace Auth_API.Tests.Users;

/// <summary>
/// Unit tests for the per-user UI preference handlers.
///
/// The limits matter more than the happy path here: this endpoint is writable
/// by any authenticated caller, so without a key namespace, a size cap and a
/// per-user count it is general-purpose storage behind a login.
/// </summary>
public class UiPreferenceHandlerTests
{
    private readonly Mock<IUserUiPreferenceRepository> _repositoryMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private SetMyUiPreferenceCommandHandler SetHandler => new(_repositoryMock.Object);

    private void GivenStored(params UserUiPreference[] preferences) =>
        _repositoryMock
            .Setup(r => r.GetAllForUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preferences);

    [Fact]
    public async Task Get_ReturnsTheUsersPreferencesAsAMap()
    {
        GivenStored(
            UserUiPreference.Create(_userId, "table:users", "{\"a\":1}"),
            UserUiPreference.Create(_userId, "table:roles", "{\"b\":2}"));

        var handler = new GetMyUiPreferencesQueryHandler(_repositoryMock.Object);
        var result = await handler.Handle(new GetMyUiPreferencesQuery(_userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["table:users"] = "{\"a\":1}",
            ["table:roles"] = "{\"b\":2}",
        });
    }

    [Fact]
    public async Task Get_ReturnsAnEmptyMapForAUserWithNoPreferences()
    {
        // First visit is the normal case, not an error.
        GivenStored();

        var handler = new GetMyUiPreferencesQueryHandler(_repositoryMock.Object);
        var result = await handler.Handle(new GetMyUiPreferencesQuery(_userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_StoresAValidPreference()
    {
        GivenStored();

        var result = await SetHandler.Handle(
            new SetMyUiPreferenceCommand(_userId, "table:users", "{\"order\":[\"a\"]}"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<UserUiPreference>(p =>
                    p.UserId == _userId &&
                    p.Key == "table:users" &&
                    p.Value == "{\"order\":[\"a\"]}"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Set_RejectsAValueThatIsNotJson()
    {
        GivenStored();

        var result = await SetHandler.Handle(
            new SetMyUiPreferenceCommand(_userId, "table:users", "not json at all"),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("UiPreference.ValueNotJson");
        _repositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<UserUiPreference>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Set_RejectsAValueOverTheLengthCap()
    {
        GivenStored();
        var oversized = "\"" + new string('x', UserUiPreference.MaxValueLength) + "\"";

        var result = await SetHandler.Handle(
            new SetMyUiPreferenceCommand(_userId, "table:users", oversized),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("UiPreference.ValueTooLarge");
    }

    [Fact]
    public async Task Set_RejectsANewKeyOnceTheUserIsAtTheLimit()
    {
        GivenStored(Enumerable
            .Range(0, UserUiPreference.MaxKeysPerUser)
            .Select(i => UserUiPreference.Create(_userId, $"table:t{i}", "{}"))
            .ToArray());

        var result = await SetHandler.Handle(
            new SetMyUiPreferenceCommand(_userId, "table:one-more", "{}"),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("UiPreference.TooManyKeys");
    }

    [Fact]
    public async Task Set_StillReplacesAnExistingKeyWhenTheUserIsAtTheLimit()
    {
        // Only a new key can push the user over; a user at the ceiling must
        // still be able to rearrange the tables they already have.
        var stored = Enumerable
            .Range(0, UserUiPreference.MaxKeysPerUser)
            .Select(i => UserUiPreference.Create(_userId, $"table:t{i}", "{}"))
            .ToArray();
        GivenStored(stored);

        var result = await SetHandler.Handle(
            new SetMyUiPreferenceCommand(_userId, "table:t0", "{\"changed\":true}"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _repositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<UserUiPreference>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("table:users")]
    [InlineData("table:org-members")]
    [InlineData("table:t1")]
    public void Validator_AcceptsTableScopedKeys(string key)
    {
        new SetMyUiPreferenceCommandValidator()
            .TestValidate(new SetMyUiPreferenceCommand(Guid.NewGuid(), key, "{}"))
            .ShouldNotHaveValidationErrorFor(c => c.Key);
    }

    [Theory]
    [InlineData("secrets")]
    [InlineData("table:")]
    [InlineData("table:Users")]
    [InlineData("table:users/../etc")]
    [InlineData("../table:users")]
    [InlineData("blob:anything")]
    public void Validator_RejectsKeysOutsideTheAllowedNamespace(string key)
    {
        new SetMyUiPreferenceCommandValidator()
            .TestValidate(new SetMyUiPreferenceCommand(Guid.NewGuid(), key, "{}"))
            .ShouldHaveValidationErrorFor(c => c.Key);
    }

    [Fact]
    public async Task Delete_RemovesTheKeyAndTreatsAMissingKeyAsSuccess()
    {
        var handler = new DeleteMyUiPreferenceCommandHandler(_repositoryMock.Object);

        var result = await handler.Handle(
            new DeleteMyUiPreferenceCommand(_userId, "table:users"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _repositoryMock.Verify(
            r => r.DeleteAsync(_userId, "table:users", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
