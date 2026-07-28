namespace Auth.Application.DTOs;

/// <summary>
/// One recorded privacy-policy revision with its notification stamp.
/// </summary>
public class PrivacyPolicyVersionDto
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime EffectiveDateUtc { get; set; }
    public DateTime? NotifiedAtUtc { get; set; }
    public int? NotifiedCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Outcome of a policy-change notification run.
/// </summary>
public class PrivacyPolicyNotifyResultDto
{
    public int RecipientCount { get; set; }
}
