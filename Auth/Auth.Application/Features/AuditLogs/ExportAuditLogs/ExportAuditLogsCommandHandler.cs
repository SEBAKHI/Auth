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
            request.ParticipantId,
            request.ParticipantRole,
            request.ApplicationId,
            request.Action,
            request.ActionType,
            request.IsSuccess,
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

        // One lookup per distinct id rather than per row. An export runs to ten
        // thousand rows and audit rows repeat their people heavily — the same
        // administrator across a whole afternoon of changes — so the uncached
        // version issued thousands of queries for a handful of answers. Resolving
        // two people per row instead of one made that worth fixing rather than
        // worth avoiding.
        var emails = new Dictionary<Guid, string?>();
        async Task<string?> EmailOf(Guid? id)
        {
            if (!id.HasValue) return null;
            if (emails.TryGetValue(id.Value, out var cached)) return cached;
            var user = await _userRepository.GetByIdAsync(id.Value, cancellationToken);
            return emails[id.Value] = user?.Email?.Value;
        }

        var applicationNames = new Dictionary<Guid, string?>();
        async Task<string?> ApplicationNameOf(Guid? id)
        {
            if (!id.HasValue) return null;
            if (applicationNames.TryGetValue(id.Value, out var cached)) return cached;
            var app = await _applicationRepository.GetByIdIncludingDeletedAsync(id.Value, cancellationToken);
            return applicationNames[id.Value] = app?.Name;
        }

        // Build export data with enriched information
        var exportData = new List<AuditLogExportRow>();
        foreach (var log in logs)
        {
            exportData.Add(new AuditLogExportRow
            {
                Id = log.Id,
                Timestamp = log.Timestamp,
                // Two people per row, never folded into one: UserId is who the
                // action happened TO, PerformedBy is who did it. A file that
                // carries only the first cannot answer who acted, which is the
                // question an export of an audit trail is taken for.
                UserId = log.UserId,
                UserEmail = await EmailOf(log.UserId),
                PerformedBy = log.PerformedBy,
                PerformedByEmail = await EmailOf(log.PerformedBy),
                ApplicationId = log.ApplicationId,
                ApplicationName = await ApplicationNameOf(log.ApplicationId),
                ActionType = log.ActionType,
                Action = log.Action,
                EntityType = log.EntityType,
                EntityId = log.EntityId,
                OldValues = log.OldValues,
                NewValues = log.NewValues,
                IpAddress = log.IpAddress,
                UserAgent = log.UserAgent,
                // Nullable all the way out. A row written before the column
                // existed has no recorded outcome, and rendering that as a
                // success is exactly what the nullable column was added to stop.
                IsSuccess = log.IsSuccess,
                ErrorMessage = log.ErrorMessage
            });
        }

        // Generate export
        byte[] content;
        string contentType;
        var stem = FileStem(request, exportData.Count, totalCount);

        if (request.Format.ToLowerInvariant() == "json")
        {
            content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
            contentType = "application/json";
        }
        else
        {
            var csv = new StringBuilder();
            // SubjectId/SubjectEmail rather than UserId/UserEmail: the column had
            // the shorter name and the reader supplied the wrong meaning, which is
            // how a file naming the locked-out employee got read as naming whoever
            // locked them out.
            csv.AppendLine(
                "Id,Timestamp,SubjectId,SubjectEmail,PerformedById,PerformedByEmail," +
                "ApplicationId,ApplicationName,ActionType,Action,EntityType,EntityId," +
                "IpAddress,Result,ErrorMessage");

            foreach (var row in exportData)
            {
                csv.AppendLine(
                    $"\"{row.Id}\"," +
                    $"\"{row.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"{row.UserId}\"," +
                    $"\"{EscapeCsv(row.UserEmail)}\"," +
                    $"\"{row.PerformedBy}\"," +
                    $"\"{EscapeCsv(row.PerformedByEmail)}\"," +
                    $"\"{row.ApplicationId}\"," +
                    $"\"{EscapeCsv(row.ApplicationName)}\"," +
                    $"\"{EscapeCsv(row.ActionType)}\"," +
                    $"\"{EscapeCsv(row.Action)}\"," +
                    $"\"{EscapeCsv(row.EntityType)}\"," +
                    $"\"{row.EntityId}\"," +
                    $"\"{EscapeCsv(row.IpAddress)}\"," +
                    // Three words, not two. An empty cell here would read as a
                    // blank rather than as "nobody recorded this".
                    $"\"{ResultText(row.IsSuccess)}\"," +
                    $"\"{EscapeCsv(row.ErrorMessage)}\"");
            }

            content = Encoding.UTF8.GetBytes(csv.ToString());
            contentType = "text/csv";
        }

        _logger.LogInformation(
            "Audit logs exported: {RecordCount} records in {Format} format by {RequestedBy}",
            exportData.Count, request.Format, request.RequestedBy);

        var fileName = $"{stem}.{(request.Format.ToLowerInvariant() == "json" ? "json" : "csv")}";

        return new ExportAuditLogsResult(content, contentType, fileName, exportData.Count, totalCount);
    }

    /// <summary>
    /// The file's name, carrying what it was narrowed by and whether it is whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every export was called <c>audit_logs_{timestamp}</c>, so an extract of
    /// one person's activity was byte-for-byte indistinguishable in provenance
    /// from an extract of the entire platform's. The scope now travels with the
    /// file, because a file outlives the request that produced it and the name
    /// is the only part of it a reader sees before opening it.
    /// </para>
    /// <para>
    /// Truncation is in the name for the same reason, and NOT as a comment row
    /// inside the CSV: a leading <c>#</c> line is read as the header by every
    /// strict parser, so disclosing it there would corrupt the file for the
    /// scripts that consume it. The requester is warned before the download by
    /// the console, which already knows the matching count; this is the copy of
    /// that warning that survives on disk.
    /// </para>
    /// </remarks>
    private static string FileStem(ExportAuditLogsCommand request, int written, int matched)
    {
        var stem = new StringBuilder("audit_logs");

        if (request.ParticipantId.HasValue)
        {
            // The role, then enough of the id to tell two exports apart. The
            // whole GUID would push the name past what a mail client shows.
            var role = (request.ParticipantRole ?? Domain.Enums.AuditParticipantRole.Subject)
                .ToString()
                .ToLowerInvariant();
            stem.Append($"_{role}_{request.ParticipantId.Value.ToString()[..8]}");
        }

        if (matched > written)
        {
            stem.Append($"_partial_{written}_of_{matched}");
        }

        stem.Append($"_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
        return stem.ToString();
    }

    /// <summary>
    /// The three states of an outcome, as a word rather than as a blank.
    /// </summary>
    /// <remarks>
    /// Not localized, on purpose: a CSV is read by a spreadsheet and a script as
    /// much as by a person, and a column whose values change with the exporter's
    /// language cannot be filtered or compared across two files. The console
    /// translates the same three states on screen.
    /// </remarks>
    private static string ResultText(bool? isSuccess) => isSuccess switch
    {
        true => "Success",
        false => "Failure",
        null => "NotRecorded"
    };

    private static string? EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\"", "\"\"");
    }

    private record AuditLogExportRow
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>Who the action happened TO — the subject, not the actor.</summary>
        public Guid? UserId { get; set; }
        public string? UserEmail { get; set; }

        /// <summary>Who PERFORMED the action. Null for a system action.</summary>
        public Guid? PerformedBy { get; set; }
        public string? PerformedByEmail { get; set; }

        public Guid? ApplicationId { get; set; }
        public string? ApplicationName { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public Guid? EntityId { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        /// <summary>True, false, or null when the outcome was never recorded.</summary>
        public bool? IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
