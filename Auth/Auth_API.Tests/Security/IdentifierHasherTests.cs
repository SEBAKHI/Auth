using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Security;

/// <summary>
/// Unit tests for <see cref="IdentifierHasher"/> — the keyed one-way hasher
/// behind the tombstone registry's permanent identifier reservations.
/// </summary>
public class IdentifierHasherTests
{
    private static IdentifierHasher CreateHasher(byte[]? key = null) =>
        new(Options.Create(new AccountDeletionSettings
        {
            IdentifierHmacKeyPlain = key is null ? null : Convert.ToBase64String(key)
        }));

    private static byte[] Key(byte fill = 0x42, int length = 32)
    {
        var key = new byte[length];
        Array.Fill(key, fill);
        return key;
    }

    [Fact]
    public void HashEmail_IsDeterministicAndCaseWhitespaceInsensitive()
    {
        var hasher = CreateHasher(Key());

        var a = hasher.HashEmail("USER@EXAMPLE.COM");
        var b = hasher.HashEmail("  user@example.com  ");

        a.Should().Be(b, "reservations must match however the identifier is later spelled");
    }

    [Fact]
    public void HashUsername_IsDeterministicAndCaseWhitespaceInsensitive()
    {
        var hasher = CreateHasher(Key());

        hasher.HashUsername("JohnDoe").Should().Be(hasher.HashUsername(" johndoe "));
    }

    [Fact]
    public void EmailAndUsernameHashSpaces_AreDomainSeparated()
    {
        var hasher = CreateHasher(Key());

        hasher.HashEmail("same-value").Should().NotBe(hasher.HashUsername("same-value"),
            "a username equal to an email string must never produce a matching hash across columns");
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentHashes()
    {
        var hasherA = CreateHasher(Key(0x01));
        var hasherB = CreateHasher(Key(0x02));

        hasherA.HashEmail("user@example.com").Should().NotBe(hasherB.HashEmail("user@example.com"));
    }

    [Fact]
    public void MissingKey_ThrowsOnFirstUse_NotOnConstruction()
    {
        // Lazy resolution: a missing key must only fail deletion operations,
        // never the construction of unrelated request pipelines.
        var hasher = CreateHasher(key: null);

        var act = () => hasher.HashEmail("user@example.com");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AccountDeletion:IdentifierHmacKeyPlain*");
    }

    [Fact]
    public void ShortKey_ThrowsOnFirstUse()
    {
        var hasher = CreateHasher(Key(length: 16));

        var act = () => hasher.HashEmail("user@example.com");

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }
}
