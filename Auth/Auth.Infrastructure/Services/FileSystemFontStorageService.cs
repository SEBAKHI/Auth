using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Infrastructure.Fonts;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Services;

/// <summary>
/// Filesystem-backed <see cref="IFontStorageService"/>. Fonts live in the uploads volume
/// (<c>ImageStorage:PhysicalPath</c>) under <c>fonts/</c>, so the static-files middleware, the
/// gateway's <c>/uploads/</c> route and <c>ImageStorage:PublicBaseUrl</c> serve them as they
/// serve images, with no setting of their own. The image ledger never sees them: its sweep
/// reclaims only keys it recorded, and an image slot cannot claim a key it did not upload.
/// </summary>
public sealed partial class FileSystemFontStorageService : IFontStorageService
{
    private const string Folder = "fonts";

    private readonly IOptionsMonitor<ImageStorageSettings> _settings;
    private readonly ILogger<FileSystemFontStorageService> _logger;

    public FileSystemFontStorageService(
        IOptionsMonitor<ImageStorageSettings> settings,
        ILogger<FileSystemFontStorageService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<ErrorOr<StoredFont>> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        // Read at most one byte past the limit: the declared length was checked already, and
        // this keeps a body that lies about it from being buffered whole.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > PlatformFonts.MaxSizeBytes)
            {
                return FontErrors.FileTooLarge(PlatformFonts.MaxSizeBytes);
            }
        }

        var bytes = buffer.ToArray();
        var font = Woff2Font.Read(bytes);
        if (font is null)
        {
            return FontErrors.Invalid;
        }

        // Named by content: the same file uploaded again is one file, and a new file is a new
        // URL, so browsers may cache every URL for good.
        var name = $"{Convert.ToHexStringLower(SHA256.HashData(bytes))}.woff2";
        var folder = FolderPath();
        var path = Path.Combine(folder, name);
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(path))
            {
                // Recent again, so a sweep of unrecorded files leaves it to the save that wants it.
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            else
            {
                // Written beside its final name and moved into place: a reader never sees half a file.
                var temp = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                try
                {
                    File.Move(temp, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // The same font, written by a save that ran beside this one.
                    File.Delete(temp);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogError(ex,
                "Font storage write failed for path {Path}. Verify the process identity has write " +
                "permission on ImageStorage:PhysicalPath ({Folder}).", path, folder);
            return FontErrors.StorageUnavailable;
        }

        return new StoredFont($"{Folder}/{name}", font.FamilyName);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        // Only a key this service minted: a stored value is never trusted as a path.
        var match = KeyPattern().Match(key);
        if (match.Success)
        {
            TryDelete(Path.Combine(FolderPath(), match.Groups["name"].Value));
        }

        return Task.CompletedTask;
    }

    public Task PruneAsync(IReadOnlyCollection<string> keep, DateTime writtenBefore, CancellationToken cancellationToken)
    {
        var folder = FolderPath();
        if (!Directory.Exists(folder))
        {
            return Task.CompletedTask;
        }

        var kept = keep
            .Select(key => KeyPattern().Match(key))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (!kept.Contains(name) && File.GetLastWriteTimeUtc(file) < writtenBefore)
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Could not sweep unrecorded font files in {Folder}", folder);
        }

        return Task.CompletedTask;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Could not delete the font file {Path}", path);
        }
    }

    private string FolderPath()
    {
        var root = _settings.CurrentValue.PhysicalPath;
        return Path.Combine(
            Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root),
            Folder);
    }

    [GeneratedRegex("^fonts/(?<name>[0-9a-f]{64}\\.woff2)$")]
    private static partial Regex KeyPattern();
}
