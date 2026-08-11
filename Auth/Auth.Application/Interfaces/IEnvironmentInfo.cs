namespace Auth.Application.Interfaces;

/// <summary>
/// Tells application code which hosting environment it is running in, without
/// taking a dependency on the hosting stack itself.
/// </summary>
/// <remarks>
/// Exists so that "developer convenience" branches can be gated on the
/// environment rather than on a setting. A setting such as <c>Email:Enabled</c>
/// is editable at runtime from the console in every environment, so a branch
/// keyed to it is one toggle away from firing in production; the environment is
/// fixed for the lifetime of the process.
/// </remarks>
public interface IEnvironmentInfo
{
    /// <summary>
    /// Gets whether the process is running in the Development environment.
    /// </summary>
    bool IsDevelopment { get; }
}
