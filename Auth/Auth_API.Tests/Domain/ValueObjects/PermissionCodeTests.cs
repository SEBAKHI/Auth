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
        result.FirstError.Code.Should().Be("PermissionCode.Empty");
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ReturnsEmptyError()
    {
        // Act
        var result = PermissionCode.Create("   ");

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PermissionCode.Empty");
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
