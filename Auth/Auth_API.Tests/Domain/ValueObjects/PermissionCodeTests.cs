using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth_API.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for the PermissionCode value object.
/// Validates creation, level calculation, wildcard detection, matching logic, and parent code resolution.
/// </summary>
public class PermissionCodeTests
{
    #region Create Tests

    [Fact]
    public void Create_WithValidCode_ReturnsPermissionCode()
    {
        // Arrange
        var input = "crm:leads:read";

        // Act
        var result = PermissionCode.Create(input);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("crm:leads:read");
    }

    [Fact]
    public void Create_WithUppercaseInput_NormalizesToLowercase()
    {
        // Arrange
        var input = "CRM:Leads:READ";

        // Act
        var result = PermissionCode.Create(input);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("crm:leads:read");
    }

    [Fact]
    public void Create_WithWhitespace_TrimsAndNormalizes()
    {
        // Arrange
        var input = "  crm:leads:read  ";

        // Act
        var result = PermissionCode.Create(input);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("crm:leads:read");
    }

    [Fact]
    public void Create_WithEmptyString_ReturnsEmptyError()
    {
        // Act
        var result = PermissionCode.Create("");

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PermissionCode.Required");
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ReturnsEmptyError()
    {
        // Act
        var result = PermissionCode.Create("   ");

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PermissionCode.Required");
    }

    [Fact]
    public void Create_Exceeding200Characters_ReturnsTooLongError()
    {
        // Arrange
        var input = new string('a', 201);

        // Act
        var result = PermissionCode.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PermissionCode.TooLong");
    }

    [Fact]
    public void Create_WithInvalidCharacters_ReturnsInvalidFormatError()
    {
        // Arrange
        var input = "crm:leads read!";

        // Act
        var result = PermissionCode.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PermissionCode.InvalidFormat");
    }

    #endregion

    #region Level Tests

    [Fact]
    public void Level_GlobalWildcard_ReturnsZero()
    {
        // Act
        var code = PermissionCode.From("*");

        // Assert
        code.Level.Should().Be(0);
    }

    [Fact]
    public void Level_SingleSegment_ReturnsOne()
    {
        // Act
        var code = PermissionCode.From("crm");

        // Assert
        code.Level.Should().Be(1);
    }

    [Fact]
    public void Level_ThreeSegments_ReturnsThree()
    {
        // Act
        var code = PermissionCode.From("crm:leads:read");

        // Assert
        code.Level.Should().Be(3);
    }

    #endregion

    #region IsWildcard Tests

    [Fact]
    public void IsWildcard_GlobalWildcard_ReturnsTrue()
    {
        // Act
        var code = PermissionCode.From("*");

        // Assert
        code.IsWildcard.Should().BeTrue();
    }

    [Fact]
    public void IsWildcard_PrefixWildcard_ReturnsTrue()
    {
        // Act
        var code = PermissionCode.From("crm:*");

        // Assert
        code.IsWildcard.Should().BeTrue();
    }

    [Fact]
    public void IsWildcard_ExactPermission_ReturnsFalse()
    {
        // Act
        var code = PermissionCode.From("crm:leads:read");

        // Assert
        code.IsWildcard.Should().BeFalse();
    }

    #endregion

    #region Matches Tests

    [Fact]
    public void Matches_ExactMatch_ReturnsTrue()
    {
        // Arrange
        var code = PermissionCode.From("crm:leads:read");

        // Act & Assert
        code.Matches("crm:leads:read").Should().BeTrue();
    }

    [Fact]
    public void Matches_GlobalWildcard_MatchesAny()
    {
        // Arrange
        var code = PermissionCode.From("*");

        // Act & Assert
        code.Matches("crm:leads:read").Should().BeTrue();
        code.Matches("billing:invoices:delete").Should().BeTrue();
    }

    [Fact]
    public void Matches_PrefixWildcard_MatchesChildren()
    {
        // Arrange
        var code = PermissionCode.From("crm:*");

        // Act & Assert
        code.Matches("crm:leads:read").Should().BeTrue();
    }

    [Fact]
    public void Matches_PrefixWildcard_MatchesBaseSegment()
    {
        // Arrange
        var code = PermissionCode.From("crm:*");

        // Act & Assert
        code.Matches("crm").Should().BeTrue();
    }

    [Fact]
    public void Matches_DifferentPermission_ReturnsFalse()
    {
        // Arrange
        var code = PermissionCode.From("crm:leads:read");

        // Act & Assert
        code.Matches("crm:leads:write").Should().BeFalse();
    }

    [Fact]
    public void Matches_PrefixWildcard_DoesNotMatchDifferentPrefix()
    {
        // Arrange
        var code = PermissionCode.From("crm:*");

        // Act & Assert
        code.Matches("billing:invoices:read").Should().BeFalse();
    }

    #endregion

    #region Format (OI-225 item 12)

    /// <remarks>Deliberate break: restore the old pattern <c>^[a-z0-9:*_\-]+$</c>.</remarks>
    [Theory]
    [InlineData("*")]
    [InlineData("crm")]
    [InlineData("crm:*")]
    [InlineData("crm:leads:read")]
    [InlineData("crm:leads:*")]
    [InlineData("notification-templates:manage")]
    [InlineData("org_x:a-b_c:read")]
    public void Create_WithWellFormedCode_Succeeds(string code)
    {
        PermissionCode.Create(code).IsError.Should().BeFalse();
    }

    /// <remarks>Deliberate break: restore the old pattern <c>^[a-z0-9:*_\-]+$</c>.</remarks>
    [Theory]
    [InlineData("*:*")]      // stripped of :* it is the global *
    [InlineData("users*")]   // * inside a segment
    [InlineData("a:*:b")]    // * before the last segment
    [InlineData("**")]
    [InlineData("crm:**")]
    [InlineData(":x")]       // empty first segment
    [InlineData("x:")]       // empty last segment
    [InlineData("crm::read")]
    public void Create_WithMalformedCode_ReturnsInvalidFormat(string code)
    {
        var result = PermissionCode.Create(code);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PermissionCode.InvalidFormat");
    }

    /// <summary>
    /// A malformed code stored before the format was enforced matches only itself: a
    /// stored <c>*:*</c> no longer covers the global <c>*</c>.
    /// </summary>
    /// <remarks>Deliberate break: drop the well-formedness check from Matches.</remarks>
    [Theory]
    [InlineData("*:*", "*", false)]
    [InlineData("*:*", "users:read", false)]
    [InlineData("*:*", "*:*", true)]
    [InlineData("users*", "users", false)]
    [InlineData("a:*:b", "a:x:b", false)]
    [InlineData("a:*:b", "a:*:b", true)]
    public void Matches_StoredMalformedCode_MatchesOnlyItself(string stored, string required, bool expected)
    {
        PermissionCode.From(stored).Matches(required).Should().Be(expected);
    }

    [Theory]
    [InlineData("*", "anything:at:all")]
    [InlineData("crm:*", "crm:leads:read")]
    [InlineData("crm:*", "crm")]
    [InlineData("crm:leads:*", "crm:leads:read")]
    [InlineData("CRM:*", "crm:leads:read")] // a stored upper-case code keeps matching case-insensitively
    public void Matches_WellFormedWildcard_KeepsItsBehaviour(string held, string required)
    {
        PermissionCode.From(held).Matches(required).Should().BeTrue();
    }

    #endregion

    #region GetParentCode Tests

    [Fact]
    public void GetParentCode_GlobalWildcard_ReturnsNull()
    {
        // Arrange
        var code = PermissionCode.From("*");

        // Act & Assert
        code.GetParentCode().Should().BeNull();
    }

    [Fact]
    public void GetParentCode_SingleSegment_ReturnsGlobalWildcard()
    {
        // Arrange
        var code = PermissionCode.From("crm");

        // Act & Assert
        code.GetParentCode().Should().Be("*");
    }

    [Fact]
    public void GetParentCode_MultiSegment_ReturnsParentWildcard()
    {
        // Arrange
        var code = PermissionCode.From("crm:leads:read");

        // Act & Assert
        code.GetParentCode().Should().Be("crm:leads:*");
    }

    #endregion

    #region From / Equality Tests

    [Fact]
    public void From_WithTrustedValue_CreatesWithoutValidation()
    {
        // Act
        var code = PermissionCode.From("crm:leads:read");

        // Assert
        code.Value.Should().Be("crm:leads:read");
    }

    [Fact]
    public void Equals_SameValue_ReturnsTrue()
    {
        // Arrange
        var code1 = PermissionCode.From("crm:leads:read");
        var code2 = PermissionCode.From("crm:leads:read");

        // Act & Assert
        code1.Equals(code2).Should().BeTrue();
        (code1 == code2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValue_ReturnsFalse()
    {
        // Arrange
        var code1 = PermissionCode.From("crm:leads:read");
        var code2 = PermissionCode.From("crm:leads:write");

        // Act & Assert
        code1.Equals(code2).Should().BeFalse();
        (code1 != code2).Should().BeTrue();
    }

    #endregion
}
