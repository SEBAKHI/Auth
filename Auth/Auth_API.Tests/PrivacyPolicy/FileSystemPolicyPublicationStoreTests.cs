using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Entities;
using Auth.Infrastructure.PrivacyPolicy;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.PrivacyPolicy;

public sealed class FileSystemPolicyPublicationStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "policy-publication-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Complete_WritesCurrentAndArchivedDocuments()
    {
        var store = CreateStore(_tempRoot);

        var staged = await store.StageAsync("2026.09", Artifacts(), CancellationToken.None);

        staged.IsError.Should().BeFalse();
        using (var publication = staged.Value)
        {
            publication.Activate().IsError.Should().BeFalse();
            publication.Complete();
        }

        File.ReadAllText(Path.Combine(_tempRoot, "ar.html")).Should().Be("<html>ar</html>");
        File.ReadAllText(Path.Combine(_tempRoot, "index.html")).Should().Be("<html>en</html>");
        File.ReadAllText(Path.Combine(_tempRoot, "v2026.09", "fa.html"))
            .Should().Be("<html>fa</html>");
        File.ReadAllText(Path.Combine(_tempRoot, "v2026.09", "index.html"))
            .Should().Be("<html>en</html>");
    }

    [Fact]
    public async Task DisposeBeforeComplete_RestoresThePreviousPublicFiles()
    {
        Directory.CreateDirectory(_tempRoot);
        var existing = Path.Combine(_tempRoot, "en.html");
        await File.WriteAllTextAsync(existing, "old-en");
        var store = CreateStore(_tempRoot);

        var staged = await store.StageAsync("2026.09", Artifacts(), CancellationToken.None);
        var publication = staged.Value;
        publication.Activate().IsError.Should().BeFalse();
        File.ReadAllText(existing).Should().Be("<html>en</html>");

        publication.Dispose();

        File.ReadAllText(existing).Should().Be("old-en");
        File.Exists(Path.Combine(_tempRoot, "ar.html")).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, "v2026.09", "ar.html")).Should().BeFalse();
    }

    [Fact]
    public async Task ActivationFailure_RollsBackFilesAlreadyReplaced()
    {
        Directory.CreateDirectory(_tempRoot);
        var existing = Path.Combine(_tempRoot, "en.html");
        await File.WriteAllTextAsync(existing, "old-en");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "tr.html"));
        var store = CreateStore(_tempRoot);

        var staged = await store.StageAsync("2026.09", Artifacts(), CancellationToken.None);
        using var publication = staged.Value;

        var result = publication.Activate();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.PublicationStorageUnavailable");
        File.ReadAllText(existing).Should().Be("old-en");
        File.Exists(Path.Combine(_tempRoot, "ar.html")).Should().BeFalse();
        Directory.Exists(Path.Combine(_tempRoot, "tr.html")).Should().BeTrue();
    }

    [Fact]
    public async Task Stage_WithMismatchedContentHash_IsRejectedBeforeWriting()
    {
        var artifacts = Artifacts();
        artifacts[0] = PrivacyPolicyArtifact.Create(
            Guid.NewGuid(), "en", "en", "<html>tampered</html>", "wrong", "style", "{}");
        var store = CreateStore(_tempRoot);

        var result = await store.StageAsync("2026.09", artifacts, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        Directory.Exists(_tempRoot).Should().BeFalse();
    }

    [Fact]
    public async Task Stage_WithInvalidVersion_IsRejectedBeforeWriting()
    {
        var store = CreateStore(_tempRoot);

        var result = await store.StageAsync("September", Artifacts(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        Directory.Exists(_tempRoot).Should().BeFalse();
    }

    [Fact]
    public async Task Stage_WithoutEverySupportedLanguage_IsRejectedBeforeWriting()
    {
        var artifacts = Artifacts();
        artifacts.RemoveAt(0);
        var store = CreateStore(_tempRoot);

        var result = await store.StageAsync("2026.09", artifacts, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        Directory.Exists(_tempRoot).Should().BeFalse();
    }

    [Fact]
    public async Task Stage_WithEmptyStoragePath_FailsWithoutWriting()
    {
        var store = CreateStore(" ");

        var result = await store.StageAsync("2026.09", Artifacts(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.PublicationStorageUnavailable");
    }

    [Fact]
    public async Task Stage_WhenStoragePathIsAFile_FailsWithoutRetrying()
    {
        Directory.CreateDirectory(_tempRoot);
        var filePath = Path.Combine(_tempRoot, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "occupied");
        var store = CreateStore(filePath);

        var result = await store.StageAsync("2026.09", Artifacts(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.PublicationStorageUnavailable");
    }

    [Fact]
    public async Task Stage_WhileAnotherPublicationIsOpen_FailsImmediately()
    {
        var store = CreateStore(_tempRoot);
        var first = await store.StageAsync("2026.09", Artifacts(), CancellationToken.None);

        var second = await store.StageAsync("2026.10", Artifacts(), CancellationToken.None);

        second.IsError.Should().BeTrue();
        second.FirstError.Code.Should().Be("PrivacyPolicy.PublicationStorageUnavailable");
        first.Value.Dispose();
    }

    private static FileSystemPolicyPublicationStore CreateStore(string path) =>
        new(
            TestHelpers.CreateOptions(new PrivacyPolicyPublicationSettings { PhysicalPath = path }),
            new Mock<ILogger<FileSystemPolicyPublicationStore>>().Object);

    private static List<PrivacyPolicyArtifact> Artifacts()
    {
        var versionId = Guid.NewGuid();
        return PolicyLanguages.Ordered
            .Select(language =>
            {
                var html = $"<html>{language}</html>";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))
                    .ToLowerInvariant();
                return PrivacyPolicyArtifact.Create(
                    versionId, language, language, html, hash, "style", "{}");
            })
            .ToList();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; a failed assertion should remain visible.
            }
        }

        GC.SuppressFinalize(this);
    }
}
