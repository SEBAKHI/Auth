using System.ComponentModel.DataAnnotations;
using Auth.Domain.Enums;

namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request to introspect a token and get its metadata.
/// </summary>
public class IntrospectTokenRequest
{
    /// <summary>
    /// The token to introspect.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Optional hint about the type of token being introspected.
    /// </summary>
    public TokenTypeHint? TokenTypeHint { get; set; }
}
