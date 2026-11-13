using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

/// <summary>
/// Issues and mails a password reset link for one account (S13, D7): the single place the three
/// ways to ask for one meet. The anonymous "forgot password" request, an administrator's "send
/// reset link", and a signed-in holder setting a password for their own account.
/// </summary>
/// <remarks>
/// One live link per account: while an unused, unexpired link exists, nothing is reissued, no
/// earlier link is invalidated and nothing is mailed, so nobody can keep the owner's link dead by
/// asking again. The callers decide what they answer; this decides what is written and sent.
/// </remarks>
public interface IPasswordResetLinkIssuer
{
    /// <summary>Issues a link for <paramref name="user"/> unless one is live.</summary>
    Task<PasswordResetLinkIssue> IssueAsync(User user, CancellationToken cancellationToken);
}

/// <summary>The outcome of one request for a link.</summary>
/// <param name="Issued">True when a new link was written and mailed; false when a live one was left in place.</param>
/// <param name="ExpiresAt">
/// The new link's expiry when one was issued; otherwise now plus the configured lifetime, the same
/// value a fresh issue would answer with, so the two cannot be told apart from the answer.
/// </param>
public sealed record PasswordResetLinkIssue(bool Issued, DateTime ExpiresAt);
