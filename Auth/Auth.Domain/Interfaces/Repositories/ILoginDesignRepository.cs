using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository of the sign-in page designs. A scope is one application's designs, or the
/// platform's (<c>applicationId</c> null).
/// </summary>
public interface ILoginDesignRepository
{
    /// <summary>The scope's designs, the active one first, then by name.</summary>
    Task<IReadOnlyList<LoginDesign>> GetByScopeAsync(Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>A design by its id, whatever its scope.</summary>
    Task<LoginDesign?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The scope's active design, or null when none is active.</summary>
    Task<LoginDesign?> GetActiveAsync(Guid? applicationId, CancellationToken cancellationToken);

    /// <summary>Stores a new design.</summary>
    Task CreateAsync(LoginDesign design, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a design's name, content, strings, logo choice and hash, only while the stored
    /// row still carries <paramref name="expectedModifiedAt"/> (or its creation time when it
    /// was never modified). Never writes <see cref="LoginDesign.IsActive"/>.
    /// </summary>
    /// <returns>False when the row changed since it was read (or is gone).</returns>
    Task<bool> TryUpdateAsync(
        LoginDesign design,
        DateTime expectedModifiedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Makes <paramref name="design"/> its scope's active design in one transaction: whichever
    /// design was active is deactivated, then this one is activated.
    /// </summary>
    Task<LoginDesignActivation> ActivateAsync(LoginDesign design, CancellationToken cancellationToken);

    /// <summary>Switches the design off while it is active.</summary>
    /// <returns>False when it was no longer active.</returns>
    Task<bool> DeactivateAsync(LoginDesign design, CancellationToken cancellationToken);

    /// <summary>Removes the design while it is inactive.</summary>
    /// <returns>False when it became active (or was gone) in the meantime.</returns>
    Task<bool> DeleteInactiveAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>The outcome of an activation.</summary>
/// <param name="Activated">True when the swap committed.</param>
/// <param name="PreviousActiveId">The design deactivated by it; null when none was active.</param>
public sealed record LoginDesignActivation(bool Activated, Guid? PreviousActiveId)
{
    /// <summary>Another activation in the same scope committed first (a unique violation).</summary>
    public static LoginDesignActivation Conflict { get; } = new(false, null);
}
