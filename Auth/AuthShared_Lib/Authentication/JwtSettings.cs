namespace AuthShared_Lib.Authentication;

public class JwtSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 120;
    public int RefreshTokenExpirationDays { get; set; } = 15;
}
