using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Helpers;

/// <summary>Sign-in page design doubles for handlers that only ask which design a page shows.</summary>
public static class LoginDesignDoubles
{
    /// <summary>No design is active anywhere: every page keeps the plain panel.</summary>
    public static EffectiveLoginDesign NoneActive() => Active(platform: null, application: null);

    /// <summary>The given designs are active: the platform's, and any one application's.</summary>
    public static EffectiveLoginDesign Active(LoginDesign? platform, LoginDesign? application)
    {
        var repository = new Mock<ILoginDesignRepository>();
        repository.Setup(r => r.GetActiveAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(platform);
        repository.Setup(r => r.GetActiveAsync(It.Is<Guid?>(id => id != null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        return new EffectiveLoginDesign(repository.Object);
    }
}
