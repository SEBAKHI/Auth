namespace Auth.Application.Configuration;

/// <summary>
/// Configuration settings for email service.
/// </summary>
public class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>
    /// Gets or sets the SMTP server host.
    /// </summary>
    public string SmtpHost { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the SMTP server port.
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Gets or sets whether to use SSL/TLS.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Gets or sets the SMTP username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the SMTP password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the sender email address.
    /// </summary>
    public string SenderEmail { get; set; } = "noreply@example.com";

    /// <summary>
    /// Gets or sets the sender display name.
    /// </summary>
    public string SenderName { get; set; } = "Auth System";

    /// <summary>
    /// Gets or sets the OTP expiration in minutes.
    /// </summary>
    public int OtpExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Gets or sets the rate limit window in seconds.
    /// </summary>
    public int RateLimitWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the maximum OTP requests per window.
    /// </summary>
    public int MaxOtpRequestsPerWindow { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether email sending is enabled.
    /// When disabled, OTPs are logged instead (for development).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the base URL of the frontend application,
    /// used to build links embedded in emails (e.g. the password reset page).
    /// </summary>
    public string FrontendBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets the rate limit window as a TimeSpan.
    /// </summary>
    public TimeSpan RateLimitWindow => TimeSpan.FromSeconds(RateLimitWindowSeconds);
}
