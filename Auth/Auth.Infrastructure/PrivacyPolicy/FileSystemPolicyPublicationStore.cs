using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.PrivacyPolicy;

/// <summary>
/// Publishes rendered privacy-policy documents to persistent filesystem storage
/// outside the Accounts deployment directory.
/// </summary>
public sealed partial class FileSystemPolicyPublicationStore : IPolicyPublicationStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IOptionsMonitor<PrivacyPolicyPublicationSettings> _settings;
    private readonly ILogger<FileSystemPolicyPublicationStore> _logger;

    public FileSystemPolicyPublicationStore(
        IOptionsMonitor<PrivacyPolicyPublicationSettings> settings,
        ILogger<FileSystemPolicyPublicationStore> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    [GeneratedRegex(@"^\d{4}\.\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern { get; }

    /// <inheritdoc />
    public async Task<ErrorOr<IPolicyFilePublication>> StageAsync(
        string version,
        IReadOnlyList<PrivacyPolicyArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var validation = Validate(version, artifacts);
        if (validation.IsError)
        {
            return validation.Errors;
        }

        string root;
        string workRoot;
        FileStream? publicationLock = null;
        try
        {
            root = ResolveRoot(_settings.CurrentValue);
            var workDirectory = Path.Combine(root, "App_Data", "policy-publication");
            Directory.CreateDirectory(workDirectory);

            // Publication has one writer. Holding an exclusive file handle
            // also protects against overlapping IIS worker processes during
            // an application-pool recycle. Contention fails immediately; this
            // is a user-initiated command and deliberately has no retry loop.
            publicationLock = new FileStream(
                Path.Combine(workDirectory, "publication.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            workRoot = Path.Combine(workDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            publicationLock?.Dispose();
            LogStorageFailure(exception, _settings.CurrentValue.PhysicalPath);
            return PrivacyPolicyErrors.PublicationStorageUnavailable;
        }

        var candidates = BuildCandidates(root, workRoot, version, artifacts);

        try
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(candidate.StagedPath)!);
                await File.WriteAllTextAsync(
                    candidate.StagedPath,
                    candidate.Html,
                    Utf8WithoutBom,
                    cancellationToken);
            }

            return new FilePublication(candidates, workRoot, publicationLock, _logger);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(workRoot, _logger);
            publicationLock.Dispose();
            throw;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            TryDeleteDirectory(workRoot, _logger);
            publicationLock.Dispose();
            LogStorageFailure(exception, root);
            return PrivacyPolicyErrors.PublicationStorageUnavailable;
        }
    }

    private static ErrorOr<Success> Validate(
        string version,
        IReadOnlyList<PrivacyPolicyArtifact> artifacts)
    {
        if (!VersionPattern.IsMatch(version))
        {
            return PrivacyPolicyErrors.InvalidContent(
                $"version '{version}' is not in YYYY.MM format");
        }

        var byLanguage = artifacts
            .GroupBy(artifact => artifact.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byLanguage.Count != PolicyLanguages.Ordered.Count ||
            byLanguage.Any(group => group.Count() != 1) ||
            PolicyLanguages.Ordered.Any(language =>
                byLanguage.All(group => !string.Equals(
                    group.Key, language, StringComparison.OrdinalIgnoreCase))))
        {
            return PrivacyPolicyErrors.InvalidContent(
                "the static publication must contain exactly one document for every supported language");
        }

        foreach (var artifact in artifacts)
        {
            var actualHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Html)))
                .ToLowerInvariant();
            if (!string.Equals(actualHash, artifact.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return PrivacyPolicyErrors.InvalidContent(
                    $"the '{artifact.LanguageCode}' document does not match its content hash");
            }
        }

        return Result.Success;
    }

    private static List<PublicationCandidate> BuildCandidates(
        string root,
        string workRoot,
        string version,
        IReadOnlyList<PrivacyPolicyArtifact> artifacts)
    {
        var candidates = new List<PublicationCandidate>((artifacts.Count + 1) * 2);
        var archiveDirectory = $"v{version}";

        foreach (var artifact in artifacts)
        {
            AddCandidate(candidates, root, workRoot, $"{artifact.LanguageCode}.html", artifact.Html);
            AddCandidate(
                candidates,
                root,
                workRoot,
                Path.Combine(archiveDirectory, $"{artifact.LanguageCode}.html"),
                artifact.Html);
        }

        var neutral = artifacts.Single(artifact => string.Equals(
            artifact.LanguageCode, PolicyLanguages.Fallback, StringComparison.OrdinalIgnoreCase));
        AddCandidate(candidates, root, workRoot, "index.html", neutral.Html);
        AddCandidate(
            candidates,
            root,
            workRoot,
            Path.Combine(archiveDirectory, "index.html"),
            neutral.Html);

        return candidates;
    }

    private static void AddCandidate(
        ICollection<PublicationCandidate> candidates,
        string root,
        string workRoot,
        string relativePath,
        string html)
    {
        candidates.Add(new PublicationCandidate(
            Path.Combine(workRoot, "new", relativePath),
            Path.Combine(root, relativePath),
            Path.Combine(workRoot, "backup", relativePath),
            html));
    }

    private static string ResolveRoot(PrivacyPolicyPublicationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PhysicalPath))
        {
            throw new ArgumentException("PrivacyPolicyPublication:PhysicalPath is empty.");
        }

        return Path.GetFullPath(
            Path.IsPathRooted(settings.PhysicalPath)
                ? settings.PhysicalPath
                : Path.Combine(AppContext.BaseDirectory, settings.PhysicalPath));
    }

    private void LogStorageFailure(Exception exception, string path) =>
        _logger.LogError(
            exception,
            "Privacy-policy publication failed for {Path}. Verify that the API process has " +
            "write access to PrivacyPolicyPublication:PhysicalPath.",
            path);

    private static bool IsStorageException(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException;

    private static void TryDeleteDirectory(
        string path,
        ILogger<FileSystemPolicyPublicationStore> logger)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            logger.LogWarning(
                exception,
                "Could not remove privacy-policy publication workspace {Path}.",
                path);
        }
    }

    private sealed class PublicationCandidate
    {
        internal PublicationCandidate(
            string stagedPath,
            string targetPath,
            string backupPath,
            string html)
        {
            StagedPath = stagedPath;
            TargetPath = targetPath;
            BackupPath = backupPath;
            Html = html;
        }

        internal string StagedPath { get; }
        internal string TargetPath { get; }
        internal string BackupPath { get; }
        internal string Html { get; }
        internal bool HadOriginal { get; set; }
        internal bool Applied { get; set; }
    }

    private sealed class FilePublication : IPolicyFilePublication
    {
        private readonly IReadOnlyList<PublicationCandidate> _candidates;
        private readonly string _workRoot;
        private readonly FileStream _publicationLock;
        private readonly ILogger<FileSystemPolicyPublicationStore> _logger;
        private bool _activated;
        private bool _completed;
        private bool _disposed;

        internal FilePublication(
            IReadOnlyList<PublicationCandidate> candidates,
            string workRoot,
            FileStream publicationLock,
            ILogger<FileSystemPolicyPublicationStore> logger)
        {
            _candidates = candidates;
            _workRoot = workRoot;
            _publicationLock = publicationLock;
            _logger = logger;
        }

        public ErrorOr<Success> Activate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activated)
            {
                throw new InvalidOperationException("The policy publication is already active.");
            }

            try
            {
                foreach (var candidate in _candidates)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(candidate.TargetPath)!);
                    candidate.HadOriginal = File.Exists(candidate.TargetPath);
                    if (candidate.HadOriginal)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(candidate.BackupPath)!);
                        File.Copy(candidate.TargetPath, candidate.BackupPath, overwrite: true);
                    }

                    File.Move(candidate.StagedPath, candidate.TargetPath, overwrite: true);
                    candidate.Applied = true;
                }

                _activated = true;
                return Result.Success;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                _logger.LogError(
                    exception,
                    "Activating the prepared privacy-policy files failed. Restoring the previous revision.");
                RestorePreviousFiles();
                return PrivacyPolicyErrors.PublicationStorageUnavailable;
            }
        }

        public void Complete()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_activated)
            {
                throw new InvalidOperationException("The policy publication has not been activated.");
            }

            _completed = true;
            TryDeleteDirectory(_workRoot, _logger);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_activated && !_completed)
            {
                RestorePreviousFiles();
            }

            TryDeleteDirectory(_workRoot, _logger);
            _publicationLock.Dispose();
            _disposed = true;
        }

        private void RestorePreviousFiles()
        {
            foreach (var candidate in _candidates.Reverse())
            {
                if (!candidate.Applied)
                {
                    continue;
                }

                try
                {
                    if (candidate.HadOriginal)
                    {
                        File.Move(candidate.BackupPath, candidate.TargetPath, overwrite: true);
                    }
                    else if (File.Exists(candidate.TargetPath))
                    {
                        File.Delete(candidate.TargetPath);
                    }

                    candidate.Applied = false;
                }
                catch (Exception exception) when (IsStorageException(exception))
                {
                    _logger.LogCritical(
                        exception,
                        "Could not restore previous privacy-policy file {Path} after a failed publish.",
                        candidate.TargetPath);
                }
            }

            _activated = false;
        }
    }
}
