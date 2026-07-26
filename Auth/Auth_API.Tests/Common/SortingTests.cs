using Auth.Application.Common;
using Auth.Application.Features.Users.GetUsers;
using Auth.Domain.Constants;
using Auth.Domain.Enums;

namespace Auth_API.Tests.Common;

/// <summary>
/// Unit tests for the in-memory <see cref="SortHelper"/> used by
/// handler-assembled list endpoints.
/// </summary>
public class SortHelperTests
{
    private sealed record Item(string Name, int Rank);

    private static readonly IReadOnlyDictionary<string, Func<Item, object?>> Selectors =
        SortHelper.Selectors<Item>(
            ("name", item => item.Name),
            ("rank", item => item.Rank));

    private static readonly List<Item> Items =
    [
        new("bravo", 3),
        new("Alpha", 1),
        new("charlie", 2)
    ];

    [Fact]
    public void Apply_NullSortBy_PreservesOriginalOrder()
    {
        var result = SortHelper.Apply(Items, null, SortDirection.Asc, Selectors);

        result.Should().ContainInOrder(Items[0], Items[1], Items[2]);
    }

    [Fact]
    public void Apply_UnknownField_PreservesOriginalOrder()
    {
        var result = SortHelper.Apply(Items, "unknown", SortDirection.Asc, Selectors);

        result.Should().ContainInOrder(Items[0], Items[1], Items[2]);
    }

    [Fact]
    public void Apply_StringField_SortsCaseInsensitively()
    {
        var result = SortHelper.Apply(Items, "name", SortDirection.Asc, Selectors);

        result.Select(i => i.Name).Should().ContainInOrder("Alpha", "bravo", "charlie");
    }

    [Fact]
    public void Apply_FieldNameIsCaseInsensitive()
    {
        var result = SortHelper.Apply(Items, "NAME", SortDirection.Asc, Selectors);

        result.Select(i => i.Name).Should().ContainInOrder("Alpha", "bravo", "charlie");
    }

    [Fact]
    public void Apply_Descending_ReversesOrder()
    {
        var result = SortHelper.Apply(Items, "rank", SortDirection.Desc, Selectors);

        result.Select(i => i.Rank).Should().ContainInOrder(3, 2, 1);
    }

    [Fact]
    public void Apply_NullValues_SortFirstAscending()
    {
        var selectors = SortHelper.Selectors<Item?>(("name", item => item?.Name));
        var items = new List<Item?> { new("beta", 1), null, new("alpha", 2) };

        var result = SortHelper.Apply(items, "name", SortDirection.Asc, selectors);

        result[0].Should().BeNull();
        result[1]!.Name.Should().Be("alpha");
    }
}

/// <summary>
/// Verifies the shared sort-field allow-list rule on a representative query
/// validator (the same rule guards every list endpoint).
/// </summary>
public class SortFieldValidationTests
{
    private readonly GetUsersQueryValidator _validator = new();

    [Fact]
    public void Validate_NullSortBy_Passes()
    {
        var result = _validator.Validate(new GetUsersQuery());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("CREATEDAT")] // case-insensitive
    [InlineData("lastLoginAt")]
    public void Validate_AllowedSortField_Passes(string sortBy)
    {
        var result = _validator.Validate(new GetUsersQuery(SortBy: sortBy));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("passwordHash")] // real column, deliberately not allow-listed
    [InlineData("phoneNumber")] // encrypted at rest (per-user AES-256-GCM), deliberately de-listed
    [InlineData("1; DROP TABLE Users--")]
    [InlineData("[CreatedAt]")]
    public void Validate_DisallowedSortField_Fails(string sortBy)
    {
        var result = _validator.Validate(new GetUsersQuery(SortBy: sortBy));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Validation.SortBy.NotAllowed");
    }

    [Fact]
    public void AllowList_MatchesDocumentedFields()
    {
        SortFields.Users.Allowed.Should().BeEquivalentTo(
        [
            "name", "displayName", "firstName", "lastName", "email",
            "status", "emailConfirmed", "phoneConfirmed",
            "twoFactorEnabled", "preferredLanguage", "timeZone",
            "createdAt", "modifiedAt", "lastLoginAt"
        ]);
    }

    [Fact]
    public void AllowLists_NeverContainSecretsOrBlobs()
    {
        // Guard against accidentally exposing sensitive or unsortable fields:
        // sorting by a secret column is an information-leak oracle, and JSON
        // blobs force full scans. See SortFields doc comment.
        string[] forbidden =
        [
            "passwordHash", "keyHash", "sessionToken", "twoFactorSecret",
            "oldValues", "newValues", "details", "additionalData",
        ];

        var allLists = new Dictionary<string, IReadOnlyList<string>>
        {
            [nameof(SortFields.Users)] = SortFields.Users.Allowed,
            [nameof(SortFields.Applications)] = SortFields.Applications.Allowed,
            [nameof(SortFields.AuditLogs)] = SortFields.AuditLogs.Allowed,
            [nameof(SortFields.OrganizationMembers)] = SortFields.OrganizationMembers.Allowed,
            [nameof(SortFields.Roles)] = SortFields.Roles.Allowed,
            [nameof(SortFields.Permissions)] = SortFields.Permissions.Allowed,
            [nameof(SortFields.ApiKeys)] = SortFields.ApiKeys.Allowed,
            [nameof(SortFields.WebhookKeys)] = SortFields.WebhookKeys.Allowed,
            [nameof(SortFields.Sessions)] = SortFields.Sessions.Allowed,
            [nameof(SortFields.ExternalProviders)] = SortFields.ExternalProviders.Allowed,
            [nameof(SortFields.UserRoles)] = SortFields.UserRoles.Allowed,
            [nameof(SortFields.UserPermissions)] = SortFields.UserPermissions.Allowed,
            [nameof(SortFields.PermissionImplications)] = SortFields.PermissionImplications.Allowed,
            [nameof(SortFields.UserOrganizations)] = SortFields.UserOrganizations.Allowed,
            [nameof(SortFields.OrganizationInvitations)] = SortFields.OrganizationInvitations.Allowed,
            [nameof(SortFields.OrganizationApplications)] = SortFields.OrganizationApplications.Allowed,
        };

        foreach (var (name, allowed) in allLists)
        {
            allowed.Should().NotContain(
                field => forbidden.Contains(field, StringComparer.OrdinalIgnoreCase),
                $"the {name} allow-list must never expose secret or blob fields");
        }
    }
}
