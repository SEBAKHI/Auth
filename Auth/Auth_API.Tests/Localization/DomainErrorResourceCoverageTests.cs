using System.Collections;
using System.Globalization;
using System.Resources;
using Auth_API.Tests.ErrorContract;
using Auth_Localization.Resources.Errors;
using Xunit;

namespace Auth_API.Tests.Localization;

/// <summary>
/// DomainErrors.resx holds exactly one sentence per published error code (ADR 0001): none
/// missing, so no problem goes out without a translatable <c>detail</c>, and none left over
/// from a code that no longer exists. That every other culture has the same keys and
/// placeholders is <see cref="BaselineCoverageTests"/>'s job.
/// </summary>
public class DomainErrorResourceCoverageTests
{
    [Fact]
    public void DomainErrorsSentences_AreExactlyThePublishedCodes()
    {
        var keys = new ResourceManager(typeof(DomainErrors).FullName!, typeof(DomainErrors).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var missing = PublishedErrorCodes.All.Keys.Where(code => !keys.Contains(code)).Order(StringComparer.Ordinal);
        var orphaned = keys.Where(key => !PublishedErrorCodes.Contains(key)).Order(StringComparer.Ordinal);

        Assert.Empty(missing);
        Assert.Empty(orphaned);
    }
}
