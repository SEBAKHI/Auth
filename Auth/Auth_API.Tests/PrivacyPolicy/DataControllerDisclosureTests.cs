using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;
using Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.PrivacyPolicy;

/// <summary>
/// A privacy policy must name its own data controller.
///
/// <para>
/// This used to be guarded only by a banner in the accounts SPA, computed from
/// a build-time constant. It could not stop a publish, and it did not describe
/// the document users actually read — that one is served from the database with
/// whatever text the seed baked in. Filling the constant changed the offline
/// fallback and nothing else.
/// </para>
/// </summary>
public class DataControllerDisclosureTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private static DataControllerSettings Complete() => new()
    {
        LegalName = "Acme Corp LLC",
        Address = "1 Example Street, Istanbul",
        PrivacyEmail = "privacy@example.com",
        EmailProvider = "Example Mail",
        HostingProvider = "Example Hosting",
        HostingCountry = "Türkiye"
    };

    [Fact]
    public void MissingRequired_IsEmpty_WhenEveryRequiredFieldIsFilled()
    {
        Complete().MissingRequired().Should().BeEmpty();
    }

    [Fact]
    public void MissingRequired_IgnoresTheOptionalFields()
    {
        var settings = Complete();
        settings.DpoContact = "";
        settings.VerbisNo = "";
        settings.KepAddress = "";

        settings.MissingRequired().Should().BeEmpty(
            "a DPO, a VERBİS number and a KEP address are conditionally required by law — " +
            "the system cannot decide for the operator whether they apply");
    }

    [Theory]
    [InlineData("LegalName")]
    [InlineData("Address")]
    [InlineData("PrivacyEmail")]
    [InlineData("EmailProvider")]
    [InlineData("HostingProvider")]
    [InlineData("HostingCountry")]
    public void MissingRequired_ReportsABlankRequiredField(string field)
    {
        var settings = Complete();
        typeof(DataControllerSettings).GetProperty(field)!.SetValue(settings, "");

        settings.MissingRequired().Should().ContainSingle().Which.Should().Be(field);
    }

    [Fact]
    public void MissingRequired_TreatsABracketedPlaceholderAsUnfilled()
    {
        var settings = Complete();
        settings.LegalName = "[LEGAL ENTITY NAME]";

        settings.MissingRequired().Should().ContainSingle().Which.Should().Be("LegalName",
            "a placeholder copied out of the old constant is not a filled-in controller, and it " +
            "would otherwise be published verbatim as the controller's name");
    }

    [Fact]
    public async Task Publish_IsRefused_WhileTheControllerIsUnnamed()
    {
        var (handler, repository) = CreatePublishHandler(new DataControllerSettings());

        var result = await handler.Handle(
            new PublishPrivacyPolicyVersionCommand("2026.09") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PrivacyPolicy.ControllerDetailsIncomplete");
        result.FirstError.Description.Should().Contain("LegalName",
            "the operator must be told which fields to fill, not just that something is wrong");

        repository.Verify(
            r => r.PublishArtifactsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<PrivacyPolicyArtifact>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "refusing must actually prevent the publish, not merely warn about it");
    }

    [Fact]
    public async Task Publish_Succeeds_OnceTheControllerIsNamed()
    {
        var (handler, repository) = CreatePublishHandler(Complete());

        var result = await handler.Handle(
            new PublishPrivacyPolicyVersionCommand("2026.09") { RequestedBy = AdminId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        repository.Verify(
            r => r.PublishArtifactsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<PrivacyPolicyArtifact>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Disclosure_CarriesEveryControllerField_SoTheDocumentCanInterpolateThem()
    {
        var disclosure = GetPublishedPrivacyPolicyQueryHandler.BuildDisclosure(
            new AccountDeletionSettings(), Complete());

        disclosure.LegalName.Should().Be("Acme Corp LLC");
        disclosure.Address.Should().Be("1 Example Street, Istanbul");
        disclosure.PrivacyEmail.Should().Be("privacy@example.com");
        disclosure.EmailProvider.Should().Be("Example Mail");
        disclosure.HostingProvider.Should().Be("Example Hosting");
        disclosure.HostingCountry.Should().Be("Türkiye");
    }

    [Fact]
    public void Disclosure_NeverCarriesNull_EvenWhenNothingIsConfigured()
    {
        // A JSON null reaches the client-side interpolate() as String(null) and
        // renders the literal word "null" inside a published legal disclosure.
        var disclosure = GetPublishedPrivacyPolicyQueryHandler.BuildDisclosure(
            new AccountDeletionSettings(), new DataControllerSettings());

        new[]
        {
            disclosure.LegalName, disclosure.Address, disclosure.PrivacyEmail,
            disclosure.EmailProvider, disclosure.HostingProvider, disclosure.HostingCountry,
            disclosure.DpoContact, disclosure.VerbisNo, disclosure.KepAddress
        }.Should().AllSatisfy(value => value.Should().NotBeNull());
    }

    private static (PublishPrivacyPolicyVersionCommandHandler Handler,
        Mock<IPrivacyPolicyVersionRepository> Repository) CreatePublishHandler(
            DataControllerSettings controller)
    {
        var version = PrivacyPolicyVersion.Create("2026.09", DateTime.UtcNow, null, AdminId);

        var (handler, repository, _, _, _, _) = PolicyPublishHarness.Create(
            version,
            controller,
            [PolicyPublishHarness.NeutralDocument(version.Id, AdminId)]);

        return (handler, repository);
    }
}
