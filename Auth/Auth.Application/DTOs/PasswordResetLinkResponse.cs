namespace Auth.Application.DTOs;

/// <summary>
/// The answer to a signed-in request for a password reset link (S13): where it went, masked, and
/// until when it works. The same shape as the anonymous forgot-password answer.
/// </summary>
/// <param name="ExpiresAt">When the link stops working (UTC).</param>
/// <param name="MaskedEmail">The address the link went to, masked.</param>
public sealed record PasswordResetLinkResponse(DateTime ExpiresAt, string MaskedEmail);
