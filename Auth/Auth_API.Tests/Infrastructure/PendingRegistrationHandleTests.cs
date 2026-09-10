using Auth.Application.Interfaces;
using Auth.Infrastructure.Security;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The handle a client holds for a pending registration is a keyed digest of
/// the address under a label of its own — stable for the address, distinct
/// from every other use of the same key.
/// </summary>
public class PendingRegistrationHandleTests
{
    [Fact]
    public void TheHandle_IsTheKeyedDigest_OfTheAddress_UnderItsOwnLabel()
    {
        var keyService = new Mock<IRefreshTokenKeyService>();
        keyService.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns<string>(message => "digest(" + message + ")");

        var handle = new PendingRegistrationHandle(keyService.Object).For("JANE@ONE.EXAMPLE");

        handle.Should().Be("digest(pending-registration-handle:v1:JANE@ONE.EXAMPLE)",
            "the label keeps a handle from ever being replayed as a code hash (otp:v1:) or a tombstone digest (email:)");
    }

    [Fact]
    public void TheHandle_IsTheSameForTheSameAddress_EveryTime()
    {
        // Stability is the point: a handle that changed when the row was
        // consumed would tell a poller that the address acquired an account.
        var keyService = new Mock<IRefreshTokenKeyService>();
        keyService.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns<string>(message => message.GetHashCode().ToString());
        var handles = new PendingRegistrationHandle(keyService.Object);

        handles.For("JANE@ONE.EXAMPLE").Should().Be(handles.For("JANE@ONE.EXAMPLE"));
        handles.For("JANE@ONE.EXAMPLE").Should().NotBe(handles.For("JANE@TWO.EXAMPLE"));
    }

    [Fact]
    public void AnEmptyAddress_IsRefused()
    {
        var handles = new PendingRegistrationHandle(Mock.Of<IRefreshTokenKeyService>());

        var act = () => handles.For(" ");

        act.Should().Throw<ArgumentException>();
    }
}
