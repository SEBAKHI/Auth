using Auth.Domain.Constants;

namespace Auth_API.Tests.Domain;

/// <summary>
/// Q6 (owner, 2026-10-31): a key the system revoked carries a reason code the console names in
/// the reader's language; a reason an administrator typed stays their text. Rows written before
/// the code existed hold the English sentence, and are read as the code.
/// </summary>
/// <remarks>Deliberate break: drop the legacy arm of <see cref="KeyRevocationReasons.Split"/>; the legacy case turns red.</remarks>
public class KeyRevocationReasonsTests
{
    [Fact]
    public void Codes_ArePinned()
    {
        // The console's mirror and every stored row name these strings.
        KeyRevocationReasons.ApplicationDeleted.Should().Be("ApplicationDeleted");
        KeyRevocationReasons.All.Should().Equal("ApplicationDeleted");
    }

    [Theory]
    [InlineData("ApplicationDeleted")]
    [InlineData("Application deleted")]
    public void Split_ASystemReason_IsTheCode_WithNoText(string stored)
    {
        KeyRevocationReasons.Split(stored).Should().Be((KeyRevocationReasons.ApplicationDeleted, (string?)null));
    }

    [Theory]
    [InlineData("Leaked in a public repository")]
    [InlineData("application deleted")]
    [InlineData("Application deleted.")]
    public void Split_AnAdministratorsReason_IsTheirText_AsTyped(string stored)
    {
        KeyRevocationReasons.Split(stored).Should().Be(((string?)null, stored));
    }

    [Fact]
    public void Split_NoReason_IsNeither()
    {
        KeyRevocationReasons.Split(null).Should().Be(((string?)null, (string?)null));
    }
}
