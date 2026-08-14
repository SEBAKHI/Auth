namespace Auth.Application.Interfaces;

/// <summary>
/// Downloads an external identity provider's profile picture and stores it through
/// <see cref="IImageStorageService"/>, returning the relative storage key.
/// <para>
/// The picture has to be copied rather than linked: the console and accounts apps serve
/// images under a <c>img-src</c> policy that names this origin only, so a provider URL
/// stored as-is renders as the initials fallback forever. Copying also survives the
/// provider rotating its URLs, and keeps the browser from announcing every page view
/// to the provider.
/// </para>
/// </summary>
public interface IExternalAvatarImporter
{
    /// <summary>
    /// Fetches <paramref name="pictureUrl"/> and stores it, returning the relative storage
    /// key — or <c>null</c> when there is nothing to import, the import is switched off, or
    /// it failed.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> rather than <c>ErrorOr</c> deliberately: the only caller is a
    /// sign-in, which must succeed whether or not an avatar came with it, so there is no
    /// failure it can act on. Diagnostics are logged by the implementation. A cancellation
    /// requested by the caller still propagates.
    /// </remarks>
    Task<string?> TryImportAsync(string? pictureUrl, CancellationToken cancellationToken);
}
