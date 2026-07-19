namespace Auth.Application.Features.Organizations.InitiateOwnershipTransfer;

/// <summary>
/// Response returned after a transfer confirmation code has been sent.
/// </summary>
/// <param name="ExpiresAt">UTC timestamp when the code expires.</param>
/// <param name="TargetEmailMasked">Masked email address the code was sent to.</param>
public record InitiateOwnershipTransferResponse(
    DateTime ExpiresAt,
    string TargetEmailMasked);
