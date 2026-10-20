using System.Text.Json.Serialization;
using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

// The application directory's answers (OI-194): their own types, never the console's DTOs, so
// a field added for the console can never reach an application. Optional members are dropped
// when null whatever the serializer's defaults are; enums are written by name (the API's
// JsonStringEnumConverter). No member names a token, its hash, or who sent an invitation.

/// <summary>An organization, as an application reading its directory sees it.</summary>
public sealed record ApplicationOrganizationResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>An absolute http(s) URL without credentials, or absent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoUrl { get; init; }
}

/// <summary>One member of an organization.</summary>
public sealed record ApplicationOrganizationMemberResponse
{
    public required Guid UserId { get; init; }

    /// <summary>The member's first and last name, or absent when they have none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    public required string Email { get; init; }

    /// <summary>An absolute http(s) URL without credentials, or absent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PictureUrl { get; init; }

    /// <summary>The code of the member's role in the organization.</summary>
    public required string MembershipRole { get; init; }

    public required DateTime JoinedAt { get; init; }

    /// <summary>
    /// The codes of the calling application's roles the member holds in this organization, empty
    /// when none. Absent when the caller may not read role assignments (<c>org:permissions:read</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ApplicationRoles { get; init; }
}

/// <summary>One invitation that carries the calling application's role.</summary>
public sealed record ApplicationOrganizationInvitationResponse
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    /// <summary>The code of the organization role the invitee joins with; absent if it cannot be read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MembershipRole { get; init; }

    /// <summary>
    /// The code of the calling application's role the invitee is given on acceptance; absent if it
    /// cannot be read.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApplicationRole { get; init; }

    /// <summary>The effective status: a pending invitation past its expiry is <c>Expired</c>.</summary>
    public required InvitationStatus Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime ExpiresAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? AcceptedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AcceptedUserId { get; init; }

    /// <summary>What acceptance did with the application role; absent until accepted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InvitationApplicationRoleOutcome? ApplicationRoleOutcome { get; init; }
}

/// <summary>One page of a directory list, in a fixed order.</summary>
public sealed record ApplicationDirectoryPage<TItem>
{
    public required IReadOnlyList<TItem> Items { get; init; }

    /// <summary>How many items the whole list holds.</summary>
    public required int TotalCount { get; init; }

    public required int PageNumber { get; init; }

    public required int PageSize { get; init; }
}
