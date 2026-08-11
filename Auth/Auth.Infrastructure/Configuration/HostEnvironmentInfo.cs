using Auth.Application.Interfaces;
using Microsoft.Extensions.Hosting;

namespace Auth.Infrastructure.Configuration;

/// <summary>
/// Adapts <see cref="IHostEnvironment"/> to <see cref="IEnvironmentInfo"/>, so
/// the Application layer can ask which environment it is in without referencing
/// the hosting stack.
/// </summary>
public class HostEnvironmentInfo : IEnvironmentInfo
{
    private readonly IHostEnvironment _hostEnvironment;

    public HostEnvironmentInfo(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    /// <inheritdoc />
    public bool IsDevelopment => _hostEnvironment.IsDevelopment();
}
