using System.ComponentModel.DataAnnotations;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The first step of a self-registration: an address, and nothing else. No
/// password, no name — those are given at completion, after the code that this
/// step mails has come back.
/// </summary>
public record StartRegistrationRequest
{
    [Required]
    [EmailAddress]
    // The columns that store an address are NVARCHAR(255); anything longer
    // would reach the INSERT and fail there, as a 500, on an anonymous endpoint.
    [StringLength(254)]
    public required string Email { get; init; }

    /// <summary>
    /// The site language the visitor is using. It decides the language of the
    /// code message and becomes the account's preferred language at completion.
    /// </summary>
    [StringLength(10)]
    public string? PreferredLanguage { get; init; }
}
