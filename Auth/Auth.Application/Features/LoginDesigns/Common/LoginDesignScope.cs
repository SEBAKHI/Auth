using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>
/// The one check every design endpoint starts with: the scope exists (an application that is
/// not deleted, or the platform), and a design named by id belongs to that scope. A design of
/// another application answers as missing, so a route cannot reach across applications.
/// </summary>
public sealed class LoginDesignScope
{
    private readonly ILoginDesignRepository _designs;
    private readonly IApplicationRepository _applications;

    public LoginDesignScope(ILoginDesignRepository designs, IApplicationRepository applications)
    {
        _designs = designs;
        _applications = applications;
    }

    /// <summary>The scope exists: the platform always does, an application unless deleted.</summary>
    public async Task<ErrorOr<Success>> EnsureAsync(Guid? applicationId, CancellationToken cancellationToken)
    {
        if (applicationId is not { } id)
        {
            return Result.Success;
        }

        return await _applications.GetByIdAsync(id, cancellationToken) is null
            ? ApplicationErrors.NotFound(id)
            : Result.Success;
    }

    /// <summary>The design, when the scope exists and the design is one of its designs.</summary>
    public async Task<ErrorOr<LoginDesign>> LoadAsync(
        Guid? applicationId,
        Guid designId,
        CancellationToken cancellationToken)
    {
        var scope = await EnsureAsync(applicationId, cancellationToken);
        if (scope.IsError)
        {
            return scope.Errors;
        }

        var design = await _designs.GetByIdAsync(designId, cancellationToken);
        return design is not null && design.BelongsTo(applicationId)
            ? design
            : LoginDesignErrors.NotFound(designId);
    }
}
