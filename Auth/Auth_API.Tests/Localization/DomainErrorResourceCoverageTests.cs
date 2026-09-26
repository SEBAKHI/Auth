using System.Globalization;
using System.Resources;
using Auth_API.Tests.ErrorContract;
using Auth_Localization.Resources.Errors;
using Xunit;

namespace Auth_API.Tests.Localization;

/// <summary>
/// Guards that every code of the error catalog has an entry in DomainErrors.resx, so no
/// error falls back to its hardcoded English description on localized requests. Every code
/// lives in the catalog (ADR 0001), so the catalog walk is the whole list.
/// </summary>
public class DomainErrorResourceCoverageTests
{
    [Fact]
    public void EveryDomainErrorCode_HasDomainErrorsResourceEntry()
    {
        var resourceManager = new ResourceManager(
            typeof(DomainErrors).FullName!,
            typeof(DomainErrors).Assembly);

        var missing = ErrorCatalog.Members()
            .Select(member => member.Error.Code)
            .Distinct()
            .Where(code => resourceManager.GetString(code, CultureInfo.InvariantCulture) is null)
            .OrderBy(code => code)
            .ToList();

        Assert.Empty(missing);
    }
}
