using Auth.Application.Interfaces;

namespace Auth.Application.Security;

/// <summary>
/// Default request-scoped implementation of <see cref="IPasswordWarningContext"/>.
/// Register with a scoped lifetime so the API result filter reads the same instance the
/// evaluator wrote to during the request.
/// </summary>
public sealed class PasswordWarningContext : IPasswordWarningContext
{
    private readonly List<PasswordWarning> _warnings = new();

    /// <inheritdoc />
    public IReadOnlyList<PasswordWarning> Warnings => _warnings;

    /// <inheritdoc />
    public void Add(PasswordWarning warning) => _warnings.Add(warning);
}
