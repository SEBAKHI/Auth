using Auth.Application.Common;
using Auth.Application.Features.Permissions.CreatePermission;
using Auth.Domain.Constants;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Validators;

/// <summary>
/// OI-225 item 12: a permission code is <c>*</c>, or segments of <c>[a-z0-9_-]</c> joined
/// by <c>:</c>, optionally ending in <c>:*</c>. The create validator, the value object and
/// the grant guard agree on it, so a <c>permissions:create</c> holder cannot create a
/// <c>*:*</c> that the guard would read as covering the global <c>*</c>.
/// </summary>
public class PermissionCodeFormatTests
{
    private readonly CreatePermissionCommandValidator _validator = new();

    private static CreatePermissionCommand Command(string code) => new(null, code, "A permission");

    /// <remarks>Deliberate break: restore the old pattern in the validator.</remarks>
    [Theory]
    [InlineData("*:*")]
    [InlineData("users*")]
    [InlineData("a:*:b")]
    [InlineData(":x")]
    [InlineData("x:")]
    public void Validator_RefusesMalformedCode(string code)
    {
        var result = _validator.Validate(Command(code));

        result.Errors.Should().ContainSingle(e => e.ErrorCode == "PermissionCode.InvalidFormat");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("crm:*")]
    [InlineData("crm:leads:read")]
    [InlineData("crm:leads:*")]
    public void Validator_AcceptsWellFormedCode(string code)
    {
        _validator.Validate(Command(code)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheValidatorAndTheValueObject_ShareOnePattern()
    {
        var validatorSource = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Application", "Validators", "Rules", "SharedValidationRules.cs"));

        validatorSource.Should().Contain(".Matches(PermissionCode.Pattern)",
            "two copies of the format drift apart without failing anything");
    }

    /// <remarks>Deliberate break: drop the well-formedness check from PermissionCode.Matches.</remarks>
    [Fact]
    public void AStoredStarColonStar_DoesNotCoverTheGlobalWildcard()
    {
        PermissionGrantGuard.Covers(["*:*"], ["*"]).Should().BeFalse();
        PermissionGrantGuard.Covers(["*"], ["*"]).Should().BeTrue();
        PermissionGrantGuard.Covers(["users:*"], ["users:read"]).Should().BeTrue();
    }

    /// <summary>
    /// Every code the API demands has the format, except the one dotted code, which the
    /// old pattern refused as well (PermissionCodes.Secrets.Manage explains the dot).
    /// </summary>
    [Fact]
    public void EveryCodeTheApiDemands_IsWellFormed_ExceptTheDocumentedDottedCode()
    {
        var demanded = PermissionEnforcement.All();

        demanded.Should().NotBeEmpty("the scan must find the demanded codes, or it proves nothing");
        demanded.Where(code => PermissionCode.Create(code).IsError).Should().Equal([PermissionCodes.Secrets.Manage]);
    }

    /// <summary>
    /// The dotted code was only ever reached by name or by the global <c>*</c>; matching a
    /// malformed code only exactly keeps both.
    /// </summary>
    [Fact]
    public void TheDottedCode_IsStillCoveredByItselfAndByTheGlobalWildcard_Only()
    {
        PermissionGrantGuard.Covers([PermissionCodes.Secrets.Manage], [PermissionCodes.Secrets.Manage]).Should().BeTrue();
        PermissionGrantGuard.Covers(["*"], [PermissionCodes.Secrets.Manage]).Should().BeTrue();
        PermissionGrantGuard.Covers(["secrets:*"], [PermissionCodes.Secrets.Manage]).Should().BeFalse();
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
