using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GetHmacKeyring;

/// <summary>
/// Query for the HMAC keyring: its keys, their states and windows, in the secrets file
/// and in this process (S11). Never key material.
/// </summary>
public record GetHmacKeyringQuery : IRequest<ErrorOr<HmacKeyringDto>>;
