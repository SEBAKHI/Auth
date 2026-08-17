using System.Text;
using System.Text.Json;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.ExportAuditLogs;

/// <summary>
/// Handler for exporting audit logs.
/// </summary>
public class ExportAuditLogsCommandHandler : IRequestHandler<ExportAuditLogsCommand, ErrorOr<ExportAuditLogsResult>>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<ExportAuditLogsCommandHandler> _logger;

    public ExportAuditLogsCommandHandler(
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        ILogger<ExportAuditLogsCommandHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<ExportAuditLogsResult>> Handle(ExportAuditLogsCommand request, CancellationToken cancellationToken)
    {
        // Validate format
        if (request.Format.ToLowerInvariant() != "csv" && request.Format.ToLowerInvariant() != "json")
        {
            return AuditLogErrors.InvalidExportFormat(request.Format);
        }

        // Get audit logs
        var (logs, totalCount) = await _auditLogRepository.GetPagedAsync(
            1,
            request.MaxRecords,
            request.UserId,
            request.ApplicationId,
            request.Action,
            request.FromDate,
            request.ToDate,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        if (totalCount > request.MaxRecords)
        {
            _logger.LogWarning(
                "Export requested for {TotalCount} records but limited to {MaxRecords}",
                totalCount, request.MaxRecords);
        }

        // Build export data with enriched information
        var exportData = new List<AuditLogExportRow>();
        foreach (var log in logs)
        {
            var row = new AuditLogExportRow
            {
                Id = log.Id,
                Timestamp = log.Timestamp,
                UserId = log.UserId,
                ApplicationId = log.ApplicationId,
                Action = log.Action,
                EntityType = log.EntityType,
                EntityId = log.EntityId,
                OldValues = log.OldValues,
                NewValues = log.NewValues,
                IpAddress = log.IpAddress,
                UserAgent = log.UserAgent
            };

            // Get user email if available
            if (log.UserId.HasValue)
            {
                var user = await _userRepository.GetByIdAsync(log.UserId.Value, cancellationToken);
                row.UserEmail = user?.Email?.Value;
            }

            // Get application name if available
            if (log.ApplicationId.HasValue)
            {
                var app = await _applicationRepository.GetByIdIncludingDeletedAsync(log.ApplicationId.Value, cancellationToken);
                row.ApplicationName = app?.Name;
            }

            exportData.Add(row);
        }

        // Generate export
        byte[] content;
        string contentType;
        string fileName;

        if (request.Format.ToLowerInvariant() == "json")
        {
            content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
            contentType = "application/json";
            fileName = $"audit_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        }
        else
        {
            var csv = new StringBuilder();
            csv.AppendLine("Id,Timestamp,UserId,UserEmail,ApplicationId,ApplicationName,Action,EntityType,EntityId,IpAddress");

            foreach (var row in exportData)
            {
                csv.AppendLine(
                    $"\"{row.Id}\"," +
                    $"\"{row.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"{row.UserId}\"," +
                    $"\"{EscapeCsv(row.UserEmail)}\"," +
                    $"\"{row.ApplicationId}\"," +
                    $"\"{EscapeCsv(row.ApplicationName)}\"," +
                    $"\"{EscapeCsv(row.Action)}\"," +
                    $"\"{EscapeCsv(row.EntityType)}\"," +
                    $"\"{row.EntityId}\"," +
                    $"\"{EscapeCsv(row.IpAddress)}\"");
            }

            content = Encoding.UTF8.GetBytes(csv.ToString());
            contentType = "text/csv";
            fileName = $"audit_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        }

        _logger.LogInformation(
            "Audit logs exported: {RecordCount} records in {Format} format by {RequestedBy}",
            exportData.Count, request.Format, request.RequestedBy);

        return new ExportAuditLogsResult(content, contentType, fileName, exportData.Count);
    }

    private static string? EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\"", "\"\"");
    }

    private record AuditLogExportRow
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid? UserId { get; set; }
        public string? UserEmail { get; set; }
        public Guid? ApplicationId { get; set; }
        public string? ApplicationName { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public Guid? EntityId { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
}
