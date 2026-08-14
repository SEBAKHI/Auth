using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.SetConnectionString;

/// <summary>
/// Stores the AuthDb connection string in the encrypted secrets file, from where
/// it overrides <c>ConnectionStrings:AuthDb</c> for every configuration layer
/// beneath it — including the environment variables supplied by web.config.
/// </summary>
/// <remarks>
/// The connection string is read once at startup and captured into a singleton
/// connection factory, so the stored value takes effect on the next restart.
/// <para>
/// A value that cannot connect is reported back but <em>not</em> refused outright
/// when <c>ForceSave</c> is set, and that asymmetry is load-bearing. Rotating the
/// database password otherwise has no valid order: change it at the server first
/// and the API loses its database, taking this very endpoint down with it; store
/// the new string first and a mandatory connect test rejects it, because the new
/// password is not live yet. Staging an as-yet-invalid value is therefore a
/// legitimate operation. Typos stay caught, because the operator has to be told
/// the connection failed and confirm regardless.
/// </para>
/// </remarks>
public class SetConnectionStringCommandHandler : IRequestHandler<SetConnectionStringCommand, ErrorOr<Success>>
{
    private const string SecretKey = "ConnectionStrings.AuthDb";

    private readonly IDpapiSecretService _secretService;
    private readonly IConnectionStringProbe _probe;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<SetConnectionStringCommandHandler> _logger;

    public SetConnectionStringCommandHandler(
        IDpapiSecretService secretService,
        IConnectionStringProbe probe,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<SetConnectionStringCommandHandler> logger)
    {
        _secretService = secretService;
        _probe = probe;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetConnectionStringCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.SetNotSupportedInPlainText;
        }

        var probe = await _probe.ProbeAsync(request.Value, cancellationToken);

        // Unparseable text can never start working, so no confirmation makes it
        // acceptable. Storing it would leave the API unable to start with no way
        // back in through this endpoint.
        if (!probe.IsWellFormed)
        {
            return SecretErrors.ConnectionStringMalformed(probe.Detail ?? string.Empty);
        }

        if (!probe.CanConnect && !request.ForceSave)
        {
            return SecretErrors.ConnectionStringUnreachable(probe.Detail ?? string.Empty);
        }

        try
        {
            await _secretService.SetSecretAsync(SecretKey, request.Value, cancellationToken);

            if (probe.CanConnect)
            {
                _logger.LogInformation(
                    "AuthDb connection string stored in the encrypted secrets file by user {UserId}. " +
                    "It takes effect on the next API restart.",
                    request.RequestedBy);
            }
            else
            {
                _logger.LogWarning(
                    "AuthDb connection string stored in the encrypted secrets file by user {UserId} " +
                    "even though no connection could be opened with it. The API will NOT start until the " +
                    "database accepts it. Recover with AUTH_IGNORE_SECRET_CONNECTIONSTRING=true.",
                    request.RequestedBy);
            }

            await PublishAuditAsync(request.RequestedBy, cancellationToken);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while storing the AuthDb connection string");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file while storing the AuthDb connection string");
            return SecretErrors.FileAccessFailed;
        }
    }

    /// <summary>
    /// Writes the audit trail without letting it fail the request.
    /// </summary>
    /// <remarks>
    /// The audit handler inserts a row through the database — the very thing this
    /// endpoint exists to repair when it is unreachable. On the designed rotation
    /// path the live credential is already dead, so an unguarded publish throws
    /// <c>SqlException</c> (neither of the exception types caught above) *after*
    /// the secret has already been written to disk. The operator would receive a
    /// 500 for an operation that succeeded, never be told a restart is required,
    /// and reproduce the same 500 on every retry while the file already held the
    /// new value.
    /// <para>
    /// So the failure is swallowed for the caller but never lost: it is logged at
    /// Error with the key name, which reaches the durable Serilog file even when
    /// no database row can be written.
    /// </para>
    /// </remarks>
    private async Task PublishAuditAsync(Guid requestedBy, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(
                new SecretValueChangedEvent(SecretKey, requestedBy), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SecretKey} was stored successfully by user {UserId} but the audit row could not be " +
                "written — most likely the database is unreachable, which is the situation this endpoint " +
                "exists to repair. This log line is the record of that change.",
                SecretKey, requestedBy);
        }
    }
}
