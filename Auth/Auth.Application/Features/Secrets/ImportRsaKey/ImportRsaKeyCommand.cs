using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ImportRsaKey;

/// <summary>
/// Command to import a caller-supplied RSA private key for JWT signing (bring-your-own-keys).
/// The matching public key is derived from the private key and stored automatically.
/// WARNING: This replaces the current signing key and invalidates ALL existing access tokens.
/// </summary>
/// <param name="PrivateKeyPem">The RSA private key in PEM format (PKCS#8 or PKCS#1).</param>
/// <param name="RequestedBy">The id of the administrator performing the import.</param>
public record ImportRsaKeyCommand(
    string PrivateKeyPem,
    Guid RequestedBy) : IRequest<ErrorOr<string>>;
