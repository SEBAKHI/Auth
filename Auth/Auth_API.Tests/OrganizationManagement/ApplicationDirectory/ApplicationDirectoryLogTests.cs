using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// T13 (OI-194): one Information line per directory read, naming the application, the
/// organization, the endpoint and the item count, and never an email address or a name.
/// </summary>
public class ApplicationDirectoryLogTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AMembersRead_LogsOneLine_WithTheApplicationOrganizationEndpointAndCount_AndNoPersonalData()
    {
        var user = TestHelpers.CreateUser(email: "reader@example.com", firstName: "Rae", lastName: "Reader");
        var application = TestHelpers.CreateApplication(code: "crm");
        application.SetAllowedScopes([OAuthScopes.OrganizationsRead], Guid.Empty);
        var organization = TestHelpers.CreateOrganization();

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByCodeAsync("crm", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var organizations = new Mock<IOrganizationRepository>();
        organizations.Setup(r => r.GetByIdAsync(organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync(organization);
        organizations.Setup(r => r.IsApplicationEnabledAsync(organization.Id, application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        organizations.Setup(r => r.GetMembershipAsync(organization.Id, user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: organization.Id, userId: user.Id));
        organizations.Setup(r => r.GetMembershipPermissionCodesAsync(organization.Id, user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([PermissionCodes.Org.MembersRead]);
        organizations.Setup(r => r.GetActiveMembersPagedAsync(organization.Id, Now, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([new ApplicationDirectoryMemberRow(Guid.NewGuid(), user.Id, "Rae", "Reader", "reader@example.com", null, "org-member", Now)], 1));

        var logger = new CapturingLogger<GetApplicationOrganizationMembersQueryHandler>();
        var handler = new GetApplicationOrganizationMembersQueryHandler(
            new ApplicationDirectoryAccess(users.Object, applications.Object, organizations.Object),
            organizations.Object,
            Mock.Of<IImageUrlComposer>(),
            new FixedTimeProvider(new DateTimeOffset(Now)),
            logger);

        var result = await handler.Handle(
            new GetApplicationOrganizationMembersQuery(new ApplicationDirectoryCaller(user.Id, "crm", "openid organizations.read"), organization.Id),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        var line = logger.Lines.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().Contain("crm").And.Contain(organization.Id.ToString()).And.Contain("members").And.Contain("(1 items)");
        line.Message.Should().NotContain("reader@example.com").And.NotContain("Rae").And.NotContain("Reader");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }
}
