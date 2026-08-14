using System.Buffers;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Services;

/// <summary>
/// Copies a provider's profile picture into this system's own image storage.
/// <para>
/// The URL arrives inside a provider-signed ID token that has already been validated,
/// so it is not attacker-chosen. The guards below are written as if it were anyway,
/// because that provenance is an assumption a future caller could break: https only,
/// no redirects followed, no cookies, no decompression, a per-call time budget, a
/// content-type pre-filter, and a byte ceiling enforced while reading.
/// </para>
/// </summary>
public sealed class ExternalAvatarImporter : IExternalAvatarImporter
{
    /// <summary>Read granularity for the capped copy. Profile pictures are tens of KB.</summary>
    private const int CopyBufferSize = 16 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IImageStorageService _imageStorage;
    private readonly IOptionsMonitor<ExternalAuthSettings> _externalAuth;
    private readonly IOptionsMonitor<ImageStorageSettings> _imageSettings;
    private readonly ILogger<ExternalAvatarImporter> _logger;

    public ExternalAvatarImporter(
        HttpClient httpClient,
        IImageStorageService imageStorage,
        IOptionsMonitor<ExternalAuthSettings> externalAuth,
        IOptionsMonitor<ImageStorageSettings> imageSettings,
        ILogger<ExternalAvatarImporter> logger)
    {
        _httpClient = httpClient;
        _imageStorage = imageStorage;
        _externalAuth = externalAuth;
        _imageSettings = imageSettings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> TryImportAsync(string? pictureUrl, CancellationToken cancellationToken)
    {
        var options = _externalAuth.CurrentValue.AvatarImport;
        if (!options.Enabled || string.IsNullOrWhiteSpace(pictureUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogDebug("Skipping avatar import: the provider picture URL is not absolute https");
            return null;
        }

        // The budget belongs to this call rather than to HttpClient.Timeout so that
        // TimeoutMs stays a hot setting instead of being frozen into the typed client.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(options.TimeoutMs));

        try
        {
            // Headers first: every check below happens before a byte of the body is read.
            using var response = await _httpClient.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, budget.Token);

            // Redirects are not followed (see the handler registration), so a 3xx lands
            // here as a plain failure — there is no hop to a second host to reason about.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Avatar import skipped: the provider returned {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var allowed = _imageSettings.CurrentValue.AllowedContentTypes;
            if (contentType is null || !allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                // Storage would reject this too; catching it here means an HTML error
                // page never gets downloaded in full just to be thrown away.
                _logger.LogWarning(
                    "Avatar import skipped: unsupported content type {ContentType}", contentType ?? "(none)");
                return null;
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > options.MaxBytes)
            {
                _logger.LogWarning(
                    "Avatar import skipped: declared size {Declared} exceeds the {Limit} byte limit",
                    declaredLength, options.MaxBytes);
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(budget.Token);
            using var buffer = new MemoryStream();
            if (!await CopyCappedAsync(source, buffer, options.MaxBytes, budget.Token))
            {
                _logger.LogWarning(
                    "Avatar import skipped: the response body exceeded the {Limit} byte limit",
                    options.MaxBytes);
                return null;
            }

            buffer.Position = 0;

            // The outer token, not the budget: the time limit is for the network fetch,
            // and the local decode and re-encode should not inherit what is left of it.
            var stored = await _imageStorage.SaveImageAsync(buffer, contentType, cancellationToken);
            if (stored.IsError)
            {
                _logger.LogWarning(
                    "Avatar import failed in storage: {ErrorCode}", stored.FirstError.Code);
                return null;
            }

            return stored.Value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The budget elapsed. A slow provider must not hold up a sign-in.
            _logger.LogWarning(
                "Avatar import timed out after {TimeoutMs}ms", options.TimeoutMs);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Avatar import failed");
            return null;
        }
    }

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/> from <paramref name="source"/>, returning
    /// false the moment the limit is passed. The running total is what enforces the ceiling:
    /// Content-Length is a claim, and a provider that omits it or lies about it would
    /// otherwise write an unbounded file.
    /// </summary>
    private static async Task<bool> CopyCappedAsync(
        Stream source, Stream destination, int maxBytes, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            var total = 0L;
            while (true)
            {
                var read = await source.ReadAsync(rented.AsMemory(0, CopyBufferSize), cancellationToken);
                if (read == 0)
                {
                    return true;
                }

                total += read;
                if (total > maxBytes)
                {
                    return false;
                }

                await destination.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
