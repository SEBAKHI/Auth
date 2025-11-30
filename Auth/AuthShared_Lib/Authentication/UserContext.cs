using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace AuthShared_Lib.Authentication;

public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId => GetClaim(Constants.ClaimTypes.UserId);
    public string? Email => GetClaim(Constants.ClaimTypes.Email);

    // TODO: Remove any backward compatibility usages
    public string? Role => GetClaim(Constants.ClaimTypes.Role); // Returns first role for backward compatibility 
    public List<string> Roles => GetClaims(Constants.ClaimTypes.Role).ToList();
    public string? PreferredLanguage => GetClaim(Constants.ClaimTypes.PreferredLanguage) ?? "en";
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public string? GetClaim(string claimType)
    {
        return User?.FindFirst(claimType)?.Value;
    }

    public IEnumerable<string> GetClaims(string claimType)
    {
        return User?.FindAll(claimType)?.Select(c => c.Value) ?? Enumerable.Empty<string>();
    }
}
