using Auth.Application.Interfaces;
using Auth.Shared.Configuration;

namespace Auth.Infrastructure.Security;

/// <summary>
/// <see cref="IKeyMaterialGenerator"/> over <see cref="KeyMaterialGenerator"/>, the one
/// generator the first start of every storage mode uses.
/// </summary>
public sealed class SharedKeyMaterialGenerator : IKeyMaterialGenerator
{
    /// <inheritdoc />
    public string GenerateHmacKeyBase64() => KeyMaterialGenerator.GenerateHmacKeyBase64();
}
