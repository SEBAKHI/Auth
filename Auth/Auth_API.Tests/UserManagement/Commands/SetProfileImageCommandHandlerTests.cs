using Auth.Application.Features.Users.SetProfileImage;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.UserManagement.Commands;

/// <summary>
/// Attaching a stored image is an ownership decision, not a formatting one.
///
/// This handler deletes the key it replaces. That is correct on its own — an
/// image nothing points at any more is waste — but the key being replaced got
/// there from an earlier call that accepted whatever the client sent. Two calls
/// therefore deleted an arbitrary file: name someone else's key, then name your
/// own, and their image is gone. Possession of a key was the whole of the claim
/// to it, and keys are handed out in a response body.
/// </summary>
public class SetProfileImageCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IImageStorageService> _storage = new();
    private readonly Mock<IUploadedImageRepository> _uploads = new();
    private readonly SetProfileImageCommandHandler _handler;

    public SetProfileImageCommandHandlerTests()
    {
        _handler = new SetProfileImageCommandHandler(_users.Object, _storage.Object, _uploads.Object);
    }

    [Fact]
    public async Task Handle_KeyTheCallerDoesNotOwn_IsRefusedAndDeletesNothing()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        // The ledger refuses: not this caller's upload, or already claimed.
        _uploads
            .Setup(r => r.TryAttachAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            new SetProfileImageCommand(userId, "somebody-elses-key.webp", userId), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Image.NotAvailable");

        // Both halves matter: the key must not be written, and nothing must be
        // deleted on the way to refusing.
        _users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _storage.Verify(s => s.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OwnKey_IsAcceptedAndReplacesTheOldOne()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        user.SetProfileImage("previous.webp", userId);

        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uploads
            .Setup(r => r.TryAttachAsync("mine.webp", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new SetProfileImageCommand(userId, "mine.webp", userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _storage.Verify(s => s.DeleteImageAsync("previous.webp", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClaimsTheKeyForTheActor_NotTheSubject()
    {
        // An administrator setting somebody else's picture uploaded the file
        // themselves, so the claim belongs to them. Keying on the subject would
        // refuse every administrative change and, worse, would let a user claim a
        // key by asking an admin to set it.
        var subjectId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _users.Setup(r => r.GetByIdAsync(subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: subjectId));
        _uploads
            .Setup(r => r.TryAttachAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _handler.Handle(
            new SetProfileImageCommand(subjectId, "uploaded-by-admin.webp", adminId), CancellationToken.None);

        _uploads.Verify(
            r => r.TryAttachAsync("uploaded-by-admin.webp", adminId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
