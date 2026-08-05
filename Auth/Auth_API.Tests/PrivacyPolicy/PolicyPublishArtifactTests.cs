using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Moq;

namespace Auth_API.Tests.PrivacyPolicy;

/// <summary>
/// Publishing is where the served document comes into existence, so these tests
/// pin the properties the public read path then relies on without checking.
/// </summary>
public class PolicyPublishArtifactTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    [Fact]
    public async Task Publishing_RendersEverySupportedLanguage()
    {
        // Every language becomes readable, not only the written ones — the read
        // path has no fallback logic precisely because publishing resolved it.
        var version = Version();
        var (handler, repository, _, _) = PolicyPublishHarness.Create(
            version, Complete(), [PolicyPublishHarness.NeutralDocument(version.Id, AdminId)]);

        IReadOnlyList<PrivacyPolicyArtifact> captured = [];
        repository
            .Setup(r => r.ReplaceArtifactsAsync(
                version.Id, It.IsAny<IReadOnlyList<PrivacyPolicyArtifact>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<PrivacyPolicyArtifact>, CancellationToken>(
                (_, artifacts, _) => captured = artifacts)
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(Command(version), CancellationToken.None);

        result.IsError.Should().BeFalse();
        captured.Should().HaveCount(7);
        captured.Select(a => a.LanguageCode)
            .Should().BeEquivalentTo(["en", "ar", "tr", "fr", "zh", "ur", "fa"]);
        captured.Should().AllSatisfy(a => a.Html.Should().StartWith("<!DOCTYPE html>"));
    }

    [Fact]
    public async Task AnUnwrittenLanguage_IsRecordedAsServingTheNeutralDocument()
    {
        var version = Version();
        var (handler, repository, _, _) = PolicyPublishHarness.Create(
            version,
            Complete(),
            [
                PolicyPublishHarness.NeutralDocument(version.Id, AdminId),
                PrivacyPolicyTranslation.Create(version.Id, "ar", "{}", AdminId)
            ]);

        IReadOnlyList<PrivacyPolicyArtifact> captured = [];
        repository
            .Setup(r => r.ReplaceArtifactsAsync(
                version.Id, It.IsAny<IReadOnlyList<PrivacyPolicyArtifact>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<PrivacyPolicyArtifact>, CancellationToken>(
                (_, artifacts, _) => captured = artifacts)
            .Returns(Task.CompletedTask);

        await handler.Handle(Command(version), CancellationToken.None);

        // Written: served as itself. Unwritten: served the neutral body, and the
        // row says so rather than leaving it indistinguishable from a translation.
        captured.Single(a => a.LanguageCode == "ar").IsLanguageFallback.Should().BeFalse();
        captured.Single(a => a.LanguageCode == "tr").IsLanguageFallback.Should().BeTrue();
        captured.Single(a => a.LanguageCode == "tr").SourceLanguageCode.Should().Be("en");
    }

    [Fact]
    public async Task ADocumentThatCannotRender_LeavesThePreviousRevisionServing()
    {
        // Ordering matters more than the error: rendering runs before the flag
        // moves, so a broken document cannot take the live policy down with it.
        var version = Version();
        var (handler, repository, _, _) = PolicyPublishHarness.Create(
            version,
            Complete(),
            [
                PolicyPublishHarness.NeutralDocument(
                    version.Id, AdminId, """{"intro":["Written by {{nonsense}}."]}""")
            ]);

        var result = await handler.Handle(Command(version), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.InvalidContent");
        repository.Verify(
            r => r.PublishAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(
            r => r.ReplaceArtifactsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PrivacyPolicyArtifact>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishingWithoutAnAccountsOrigin_IsRefused()
    {
        // The deletion entry point is an absolute link required by app-store
        // data-deletion policies; publishing without an origin publishes a dead
        // link into a legal document.
        var version = Version();
        var (handler, repository, _, _) = PolicyPublishHarness.Create(
            version,
            Complete(),
            [PolicyPublishHarness.NeutralDocument(version.Id, AdminId)],
            accountsBaseUrl: "");

        var result = await handler.Handle(Command(version), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Description.Should().Contain("AccountsBaseUrl");
        repository.Verify(
            r => r.PublishAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheCacheIsReplacedRatherThanLeftToRefetch()
    {
        // Evicting would make the first reader after a publish go to the
        // database, turning a database blip at that moment into a broken page.
        var version = Version();
        var (handler, _, _, cache) = PolicyPublishHarness.Create(
            version, Complete(), [PolicyPublishHarness.NeutralDocument(version.Id, AdminId)]);

        await handler.Handle(Command(version), CancellationToken.None);

        cache.Verify(
            c => c.ReplacePublished(It.Is<IReadOnlyList<PrivacyPolicyArtifact>>(a => a.Count == 7)),
            Times.Once);
    }

    [Fact]
    public async Task TheFrozenDisclosureIsStoredWithEveryDocument()
    {
        // What was frozen has to be recoverable, or the console cannot tell an
        // operator that the published text no longer describes the system.
        var version = Version();
        var (handler, repository, _, _) = PolicyPublishHarness.Create(
            version, Complete(), [PolicyPublishHarness.NeutralDocument(version.Id, AdminId)]);

        IReadOnlyList<PrivacyPolicyArtifact> captured = [];
        repository
            .Setup(r => r.ReplaceArtifactsAsync(
                version.Id, It.IsAny<IReadOnlyList<PrivacyPolicyArtifact>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<PrivacyPolicyArtifact>, CancellationToken>(
                (_, artifacts, _) => captured = artifacts)
            .Returns(Task.CompletedTask);

        await handler.Handle(Command(version), CancellationToken.None);

        captured.Should().AllSatisfy(a =>
        {
            a.DisclosureJson.Should().Contain("Acme Corp LLC");
            a.ContentHash.Should().HaveLength(64);
        });
    }

    private static PrivacyPolicyVersion Version() =>
        PrivacyPolicyVersion.Create("2026.09", DateTime.UtcNow, null, AdminId);

    private static PublishPrivacyPolicyVersionCommand Command(PrivacyPolicyVersion version) =>
        new(version.Version) { RequestedBy = AdminId };

    private static DataControllerSettings Complete() => new()
    {
        LegalName = "Acme Corp LLC",
        Address = "1 Example Street, Istanbul",
        PrivacyEmail = "privacy@example.com",
        EmailProvider = "Example Mail",
        HostingProvider = "Example Hosting",
        HostingCountry = "Türkiye"
    };
}
