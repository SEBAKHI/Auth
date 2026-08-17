using Auth.Application.Features.AuditLogs.GetAuditLogs;
using Auth.Application.Features.AuditLogs.GetAuditLogById;
using Auth.Application.Features.AuditLogs.GetAuditLogsByUser;
using Auth.Application.Features.AuditLogs.GetAuditLogsByEntity;
using Auth.Application.Features.AuditLogs.ExportAuditLogs;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.AuditLogManagement;

public class GetAuditLogsQueryHandlerTests
{
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly GetAuditLogsQueryHandler _handler;

    public GetAuditLogsQueryHandlerTests()
    {
        _handler = new GetAuditLogsQueryHandler(
            _auditLogRepoMock.Object,
            _userRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<GetAuditLogsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidQuery_ReturnsPagedResults()
    {
        var logs = new List<AuditLog>
        {
            TestHelpers.CreateAuditLog(actionType: "Authentication", action: "user.login"),
            TestHelpers.CreateAuditLog(actionType: "UserManagement", action: "user.created")
        };

        _auditLogRepoMock
            .Setup(r => r.GetPagedAsync(1, 50, null, null, null, null, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((logs as IReadOnlyList<AuditLog>, 2));

        var result = await _handler.Handle(new GetAuditLogsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Logs.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
        result.Value.PageNumber.Should().Be(1);
    }

    /// <summary>
    /// Every filter the query still accepts reaches the repository.
    /// </summary>
    /// <remarks>
    /// This test used to pass <c>ActionType: "Security"</c> and assert that the
    /// string arrived at the repository — which it faithfully did, and which
    /// meant nothing: the repository accepted the parameter and never put it in
    /// the WHERE clause, because AuditLogs has no ActionType column. Filtering
    /// by it returned the unfiltered page while this test stayed green, and both
    /// the filter and the column are now gone.
    ///
    /// The lesson is in what it asserted. Proving a value is HANDED OVER is not
    /// proving it is HONOURED; a test one layer short of the effect can only
    /// confirm the plumbing it was written from.
    /// </remarks>
    [Fact]
    public async Task Handle_WithFilters_PassesFiltersToRepository()
    {
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var query = new GetAuditLogsQuery(
            PageNumber: 2,
            PageSize: 10,
            UserId: userId,
            ApplicationId: applicationId,
            Action: "user.login",
            FromDate: from,
            ToDate: to);

        _auditLogRepoMock
            .Setup(r => r.GetPagedAsync(2, 10, userId, applicationId, "user.login", from, to, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog>() as IReadOnlyList<AuditLog>, 0));

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _auditLogRepoMock.Verify(
            r => r.GetPagedAsync(2, 10, userId, applicationId, "user.login", from, to, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }
}

public class GetAuditLogByIdQueryHandlerTests
{
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly GetAuditLogByIdQueryHandler _handler;

    public GetAuditLogByIdQueryHandlerTests()
    {
        _handler = new GetAuditLogByIdQueryHandler(
            _auditLogRepoMock.Object,
            _userRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<GetAuditLogByIdQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsAuditLogDto()
    {
        var logId = Guid.NewGuid();
        var log = TestHelpers.CreateAuditLog(id: logId, actionType: "Authentication", action: "user.login");

        _auditLogRepoMock.Setup(r => r.GetByIdAsync(logId, It.IsAny<CancellationToken>())).ReturnsAsync(log);

        var result = await _handler.Handle(new GetAuditLogByIdQuery(logId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Id.Should().Be(logId);
        result.Value.ActionType.Should().Be("Authentication");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        _auditLogRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AuditLog?)null);

        var result = await _handler.Handle(new GetAuditLogByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class GetAuditLogsByUserQueryHandlerTests
{
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly GetAuditLogsByUserQueryHandler _handler;

    public GetAuditLogsByUserQueryHandlerTests()
    {
        _handler = new GetAuditLogsByUserQueryHandler(
            _auditLogRepoMock.Object,
            _userRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<GetAuditLogsByUserQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUserId_ReturnsUserAuditLogs()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var logs = new List<AuditLog>
        {
            TestHelpers.CreateAuditLog(userId: userId, action: "user.login")
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auditLogRepoMock
            .Setup(r => r.GetPagedAsync(1, 50, userId, null, null, null, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((logs as IReadOnlyList<AuditLog>, 1));

        var result = await _handler.Handle(new GetAuditLogsByUserQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Logs.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(1);
    }
}

public class GetAuditLogsByEntityQueryHandlerTests
{
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly GetAuditLogsByEntityQueryHandler _handler;

    public GetAuditLogsByEntityQueryHandlerTests()
    {
        _handler = new GetAuditLogsByEntityQueryHandler(
            _auditLogRepoMock.Object,
            _userRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<GetAuditLogsByEntityQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidEntity_ReturnsEntityAuditLogs()
    {
        var entityId = Guid.NewGuid();
        var logs = new List<AuditLog>
        {
            TestHelpers.CreateAuditLog(entityType: "User", entityId: entityId, action: "user.created"),
            TestHelpers.CreateAuditLog(entityType: "User", entityId: entityId, action: "user.updated")
        };

        _auditLogRepoMock
            .Setup(r => r.GetByEntityAsync("User", entityId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        var result = await _handler.Handle(
            new GetAuditLogsByEntityQuery("User", entityId),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
    }
}

public class ExportAuditLogsCommandHandlerTests
{
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly ExportAuditLogsCommandHandler _handler;

    public ExportAuditLogsCommandHandlerTests()
    {
        _handler = new ExportAuditLogsCommandHandler(
            _auditLogRepoMock.Object,
            _userRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<ExportAuditLogsCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CsvFormat_ReturnsExportResult()
    {
        var logs = new List<AuditLog>
        {
            TestHelpers.CreateAuditLog(action: "user.login"),
            TestHelpers.CreateAuditLog(action: "user.created")
        };

        _auditLogRepoMock
            .Setup(r => r.GetPagedAsync(1, 10000, null, null, null, null, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((logs as IReadOnlyList<AuditLog>, 2));

        var result = await _handler.Handle(
            new ExportAuditLogsCommand("csv") { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ContentType.Should().Be("text/csv");
        result.Value.RecordCount.Should().Be(2);
        result.Value.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_NoRecords_ReturnsEmptyExport()
    {
        _auditLogRepoMock
            .Setup(r => r.GetPagedAsync(1, 10000, null, null, null, null, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog>() as IReadOnlyList<AuditLog>, 0));

        var result = await _handler.Handle(
            new ExportAuditLogsCommand("csv") { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecordCount.Should().Be(0);
    }
}
