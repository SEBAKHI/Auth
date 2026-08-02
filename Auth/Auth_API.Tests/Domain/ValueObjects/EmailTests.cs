using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth_API.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for the Email value object.
/// Validates creation, normalization, equality, and implicit conversion behavior.
/// </summary>
public class EmailTests
{
    #region Create Tests

    [Fact]
    public void Create_WithValidEmail_ReturnsEmail()
    {
        // Arrange
        var input = "User@Example.COM";

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("user@example.com");
    }

    [Fact]
    public void Create_WithWhitespace_TrimsAndNormalizes()
    {
        // Arrange
        var input = "  Test@Domain.Org  ";

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be("test@domain.org");
    }

    [Fact]
    public void Create_WithEmptyString_ReturnsEmptyError()
    {
        // Arrange
        var input = "";

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Email.Empty");
    }

    [Fact]
    public void Create_WithWhitespaceOnly_ReturnsEmptyError()
    {
        // Arrange
        var input = "   ";

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Email.Empty");
    }

    [Fact]
    public void Create_WithExceeding254Characters_ReturnsTooLongError()
    {
        // Arrange
        var input = new string('a', 243) + "@example.com"; // 255 chars

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Email.TooLong");
    }

    [Fact]
    public void Create_WithInvalidFormat_ReturnsInvalidFormatError()
    {
        // Arrange
        var input = "not-an-email";

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Email.InvalidFormat");
    }

    [Fact]
    public void Create_WithMissingDomain_ReturnsInvalidFormatError()
    {
        // Arrange
        var input = "user@";

        // Act
        var result = Email.Create(input);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Email.InvalidFormat");
    }

    #endregion

    #region From / FromNullable Tests

    [Fact]
    public void From_WithTrustedValue_CreatesEmailWithoutValidation()
    {
        // Arrange
        var input = "already-lowercase@db.com";

        // Act
        var email = Email.From(input);

        // Assert
        email.Value.Should().Be(input);
    }

    [Fact]
    public void FromNullable_WithNullInput_ReturnsNull()
    {
        // Act
        var email = Email.FromNullable(null);

        // Assert
        email.Should().BeNull();
    }

    [Fact]
    public void FromNullable_WithNonNullInput_ReturnsEmail()
    {
        // Arrange
        var input = "test@example.com";

        // Act
        var email = Email.FromNullable(input);

        // Assert
        email.Should().NotBeNull();
        email!.Value.Should().Be(input);
    }

    #endregion

    #region ToNormalized / Implicit Operator / Equality Tests

    [Fact]
    public void ToNormalized_ReturnsUppercaseValue()
    {
        // Arrange
        var email = Email.From("user@example.com");

        // Act
        var normalized = email.ToNormalized();

        // Assert
        normalized.Should().Be("USER@EXAMPLE.COM");
    }

    [Fact]
    public void ImplicitOperator_ReturnsUnderlyingValue()
    {
        // Arrange
        var email = Email.From("user@example.com");

        // Act
        string result = email;

        // Assert
        result.Should().Be("user@example.com");
    }

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        // Arrange
        var email1 = Email.From("user@example.com");
        var email2 = Email.From("user@example.com");

        // Act & Assert
        email1.Equals(email2).Should().BeTrue();
        (email1 == email2).Should().BeTrue();
        (email1 != email2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithDifferentValue_ReturnsFalse()
    {
        // Arrange
        var email1 = Email.From("a@example.com");
        var email2 = Email.From("b@example.com");

        // Act & Assert
        email1.Equals(email2).Should().BeFalse();
        (email1 == email2).Should().BeFalse();
        (email1 != email2).Should().BeTrue();
    }

    #endregion
}
