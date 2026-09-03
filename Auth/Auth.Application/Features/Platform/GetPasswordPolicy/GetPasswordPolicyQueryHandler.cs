using Auth.Application.Configuration;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Platform.GetPasswordPolicy;

/// <summary>
/// Answers the public password-policy query from the live settings.
/// <para>
/// <see cref="IOptionsSnapshot{TOptions}"/> rather than <c>IOptions</c> on
/// purpose: the policy is editable from the console without a restart (System
/// Settings overrides the file), and <c>PasswordValidator</c> reads the same
/// snapshot, so the form a person is filling in and the validator that judges
/// their submission always describe one policy.
/// </para>
/// </summary>
public class GetPasswordPolicyQueryHandler : IRequestHandler<GetPasswordPolicyQuery, ErrorOr<PasswordPolicyDto>>
{
    private readonly PasswordSettings _settings;

    public GetPasswordPolicyQueryHandler(IOptionsSnapshot<PasswordSettings> settings)
    {
        _settings = settings.Value;
    }

    public Task<ErrorOr<PasswordPolicyDto>> Handle(GetPasswordPolicyQuery request, CancellationToken cancellationToken)
    {
        ErrorOr<PasswordPolicyDto> policy = new PasswordPolicyDto
        {
            MinimumLength = _settings.MinimumLength,
            RequireUppercase = _settings.RequireUppercase,
            RequireLowercase = _settings.RequireLowercase,
            RequireDigit = _settings.RequireDigit,
            RequireSpecialCharacter = _settings.RequireSpecialCharacter
        };

        return Task.FromResult(policy);
    }
}
