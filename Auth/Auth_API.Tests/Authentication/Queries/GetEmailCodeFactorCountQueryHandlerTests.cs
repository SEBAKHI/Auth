using Auth.Application.Features.SystemSettings.GetEmailCodeFactorCount;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// S24 PR B (ARR-0694): the count the console shows before email sending is switched
/// off is the store's one count — the posture warning's — and nothing else.
/// </summary>
public class GetEmailCodeFactorCountQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsTheStoresCount()
    {
        var store = new Mock<ITwoFactorStateStore>();
        store.Setup(s => s.CountEmailCodeFactorsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var result = await new GetEmailCodeFactorCountQueryHandler(store.Object)
            .Handle(new GetEmailCodeFactorCountQuery(), CancellationToken.None);

        result.Value.Count.Should().Be(7);
        store.Verify(s => s.CountEmailCodeFactorsAsync(It.IsAny<CancellationToken>()), Times.Once);
        store.VerifyNoOtherCalls();
    }
}
