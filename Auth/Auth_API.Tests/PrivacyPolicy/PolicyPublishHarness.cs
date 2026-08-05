using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.PrivacyPolicy;
using Auth_API.Tests.Helpers;
using Moq;

namespace Auth_API.Tests.PrivacyPolicy;

/// <summary>
/// Builds the publish handler for tests.
///
/// Wires the REAL renderer rather than a mock: publishing now produces the exact
/// bytes the public is served, so a test that stubs the renderer would assert
/// the flag moved while proving nothing about the document — which is precisely
/// the gap that let a placeholder reach a published page.
/// </summary>
internal static class PolicyPublishHarness
{
    internal const string AccountsBaseUrl = "https://accounts.example.com";

    /// <summary>A document with no tokens, so rendering depends only on the wiring.</summary>
    internal const string MinimalDocument = "{}";

    internal static (PublishPrivacyPolicyVersionCommandHandler Handler,
        Mock<IPrivacyPolicyVersionRepository> Repository,
        Mock<IAuditLogRepository> Audit,
        Mock<IPolicyArtifactCache> Cache) Create(
            PrivacyPolicyVersion version,
            DataControllerSettings controller,
            IReadOnlyList<PrivacyPolicyTranslation>? translations = null,
            string accountsBaseUrl = AccountsBaseUrl)
    {
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository
            .Setup(r => r.GetByVersionAsync(version.Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        repository
            .Setup(r => r.GetTranslationsAsync(version.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(translations ?? []);

        var audit = new Mock<IAuditLogRepository>();
        var cache = new Mock<IPolicyArtifactCache>();

        var handler = new PublishPrivacyPolicyVersionCommandHandler(
            repository.Object,
            audit.Object,
            new PolicyDocumentRenderer(),
            cache.Object,
            TestHelpers.CreateOptions(controller),
            TestHelpers.CreateOptions(new AccountDeletionSettings()),
            TestHelpers.CreateOptions(new IdentityProviderSettings
            {
                AccountsBaseUrl = accountsBaseUrl
            }));

        return (handler, repository, audit, cache);
    }

    /// <summary>The neutral document, which publishing requires.</summary>
    internal static PrivacyPolicyTranslation NeutralDocument(
        Guid versionId, Guid authorId, string contentJson = MinimalDocument) =>
        PrivacyPolicyTranslation.Create(versionId, "en", contentJson, authorId);
}
