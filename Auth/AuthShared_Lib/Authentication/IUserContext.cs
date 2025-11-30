namespace AuthShared_Lib.Authentication;

public interface IUserContext
{
    string? UserId { get; }
    string? Email { get; }
    string? Role { get; }
    List<string> Roles { get; }
    string? PreferredLanguage { get; }
    bool IsAuthenticated { get; }
    string? GetClaim(string claimType);
    IEnumerable<string> GetClaims(string claimType);
}
