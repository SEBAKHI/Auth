using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Security;

/// <summary>
/// Unit tests for <see cref="PerUserCryptoService"/> — AES-256-GCM field
/// encryption under lazily-created per-user DEKs with purpose/user AAD binding.
/// </summary>
public class PerUserCryptoServiceTests
{
    private const string Purpose = EncryptedFieldPurpose.UserPhoneNumber;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly EphemeralDataProtectionProvider _provider = new();
    private readonly InMemoryUserEncryptionKeyRepository _keyRepository = new();

    /// <summary>
    /// Fresh cache per service so shred/race scenarios control exactly what a
    /// service instance can see; the provider is shared so every service can
    /// unwrap DEKs created by another.
    /// </summary>
    private PerUserCryptoService CreateService(IUserEncryptionKeyRepository? repository = null) =>
        new(repository ?? _keyRepository,
            _provider,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<ILogger<PerUserCryptoService>>().Object);

    [Fact]
    public async Task EncryptDecrypt_RoundTripsWithV2Prefix()
    {
        var service = CreateService();

        var ciphertext = await service.EncryptAsync(_userId, "+90 555 123 4567", Purpose, CancellationToken.None);

        ciphertext.Should().StartWith("v2:");
        ciphertext.Should().NotContain("555");
        (await service.DecryptAsync(_userId, ciphertext, Purpose, CancellationToken.None))
            .Should().Be("+90 555 123 4567");
    }

    [Fact]
    public async Task Encrypt_CreatesTheDekLazily_ExactlyOnce()
    {
        var service = CreateService();

        await service.EncryptAsync(_userId, "one", Purpose, CancellationToken.None);
        await service.EncryptAsync(_userId, "two", Purpose, CancellationToken.None);

        _keyRepository.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Decrypt_TamperedCiphertext_FailsClosed()
    {
        var service = CreateService();
        var ciphertext = await service.EncryptAsync(_userId, "secret", Purpose, CancellationToken.None);

        var payload = Convert.FromBase64String(ciphertext["v2:".Length..]);
        payload[^1] ^= 0xFF;
        var tampered = "v2:" + Convert.ToBase64String(payload);

        var act = () => service.DecryptAsync(_userId, tampered, Purpose, CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Decrypt_WrongPurpose_FailsClosed()
    {
        var service = CreateService();
        var ciphertext = await service.EncryptAsync(_userId, "secret", Purpose, CancellationToken.None);

        var act = () => service.DecryptAsync(
            _userId, ciphertext, EncryptedFieldPurpose.TwoFactorSecretKey, CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>(
            "ciphertext must never be transplantable between fields");
    }

    [Fact]
    public async Task Decrypt_DifferentUsersKey_FailsClosed()
    {
        var service = CreateService();
        var otherUserId = Guid.NewGuid();
        await service.EncryptAsync(otherUserId, "prime other user's DEK", Purpose, CancellationToken.None);
        var ciphertext = await service.EncryptAsync(_userId, "secret", Purpose, CancellationToken.None);

        var act = () => service.DecryptAsync(otherUserId, ciphertext, Purpose, CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>(
            "ciphertext must never be transplantable between users");
    }

    [Fact]
    public async Task Decrypt_AfterCryptoShred_FailsClosed()
    {
        var writer = CreateService();
        var ciphertext = await writer.EncryptAsync(_userId, "secret", Purpose, CancellationToken.None);

        await _keyRepository.DeleteByUserIdAsync(_userId, CancellationToken.None);
        var reader = CreateService(); // fresh cache: sees the shredded state

        var act = () => reader.DecryptAsync(_userId, ciphertext, Purpose, CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>().WithMessage("*unrecoverable*");
    }

    [Fact]
    public async Task Decrypt_NonV2Value_ThrowsInvalidOperation()
    {
        var service = CreateService();

        var act = () => service.DecryptAsync(_userId, "plaintext-legacy", Purpose, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "dual-read callers must check IsEncrypted before decrypting");
    }

    [Theory]
    [InlineData("v2:abc", true)]
    [InlineData("plain +90 555", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEncrypted_ChecksThePrefix(string? value, bool expected)
    {
        CreateService().IsEncrypted(value).Should().Be(expected);
    }

    [Fact]
    public async Task CreateRace_LosingWriter_UsesTheWinnersKey()
    {
        // The winner's DEK already exists in the shared store.
        var winnerService = CreateService();
        await winnerService.EncryptAsync(_userId, "winner priming", Purpose, CancellationToken.None);

        // The loser observes "no key" first, collides on create, and must
        // fall back to the winner's row.
        var racingRepository = new RacingRepository(_keyRepository);
        var losingService = CreateService(racingRepository);

        var ciphertext = await losingService.EncryptAsync(_userId, "raced value", Purpose, CancellationToken.None);

        (await winnerService.DecryptAsync(_userId, ciphertext, Purpose, CancellationToken.None))
            .Should().Be("raced value", "the losing writer must encrypt under the winner's DEK");
    }

    /// <summary>
    /// Simulates losing the create race: the first lookup misses, the insert
    /// collides, and only the re-read surfaces the winner's key.
    /// </summary>
    private sealed class RacingRepository(IUserEncryptionKeyRepository inner) : IUserEncryptionKeyRepository
    {
        private bool _firstGet = true;

        public Task<UserEncryptionKey?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (_firstGet)
            {
                _firstGet = false;
                return Task.FromResult<UserEncryptionKey?>(null);
            }

            return inner.GetByUserIdAsync(userId, cancellationToken);
        }

        public Task CreateAsync(UserEncryptionKey key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Duplicate key (unique constraint).");

        public Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.DeleteByUserIdAsync(userId, cancellationToken);
    }
}
