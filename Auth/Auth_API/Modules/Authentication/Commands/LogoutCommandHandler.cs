using System.Security.Cryptography;
using System.Text;
using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the logout command.
/// </summary>
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, ErrorOr<Success>>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ILogger<LogoutCommandHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (request.LogoutAllDevices)
        {
            // Revoke all tokens for the user
            await _refreshTokenRepository.RevokeAllForUserAsync(
                request.UserId,
                request.UserId, // revokedBy - user initiated
                "User initiated logout from all devices",
                cancellationToken);

            _logger.LogInformation("User {UserId} logged out from all devices", request.UserId);
        }
        else if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            // Revoke only the specific token
            var tokenHash = ComputeSha256Hash(request.RefreshToken);
            var token = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

            if (token != null && !token.IsRevoked)
            {
                token.Revoke(request.UserId, "User initiated logout");
                await _refreshTokenRepository.UpdateAsync(token, cancellationToken);

                _logger.LogInformation("User {UserId} logged out from single device", request.UserId);
            }
        }
        else
        {
            // No specific token provided, just acknowledge the logout
            _logger.LogInformation("User {UserId} logout acknowledged (no token to revoke)", request.UserId);
        }

        return Result.Success;
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
