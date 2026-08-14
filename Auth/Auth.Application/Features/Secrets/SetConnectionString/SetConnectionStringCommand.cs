using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.SetConnectionString;

/// <summary>
/// Command to store the AuthDb connection string in the encrypted secrets file,
/// where it overrides <c>ConnectionStrings:AuthDb</c> from configuration.
/// </summary>
/// <param name="Value">The full connection string.</param>
/// <param name="ForceSave">
/// Store the value even though no connection could be opened with it. Required
/// for the one legitimate case: staging a password that has not been switched
/// over at the database server yet.
/// </param>
/// <param name="RequestedBy">The administrator performing the change.</param>
public record SetConnectionStringCommand(
    string Value,
    bool ForceSave,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
