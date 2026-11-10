using System.Buffers.Text;
using System.Security.Claims;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.GetMyPasskeys;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR C (IN 12, D-S20-7): the owner's list answers, beside each live passkey of the RP ID in
/// force, its credential id, and with them the RP ID and the account's user handle, so the
/// security page can tell the browser which passkeys the account still has. All base64url
/// without padding, as WebAuthn writes them. The handle is null before the account's first
/// passkey, and read even when no passkey is left (the empty list is a signal too). Nothing of
/// it is logged: the handler has no logger, and <see cref="PasskeyLogContentGuardTests"/> still
/// finds no forbidden name in a log call.
/// </summary>
/// <remarks>Deliberate break: answer the handle as null always (drop its read): red.</remarks>
public class GetMyPasskeysQueryHandlerTests
{
    private static readonly Guid Owner = Guid.Parse("8a1d4c2e-0000-4000-8000-000000000001");
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    private static UserPasskey Passkey(byte[] credentialId, string name, DateTime createdAt) =>
        UserPasskey.Create(Owner, credentialId, [9, 9, 9], 0, PasskeyTestDoubles.RpId, true, true, true, name, createdAt).Value;

    [Fact]
    public async Task AnswersEachPasskeysCredentialId_TheUserHandle_AndTheRpIdInForce()
    {
        byte[] laptop = [0xFB, 0xFF, 0x01, 0x02];
        byte[] phone = [0x00, 0x3E, 0x3F, 0x40, 0x41];
        var handle = Enumerable.Range(0, 64).Select(i => (byte)(255 - i)).ToArray();
        var rows = new Mock<IUserPasskeyRepository>();
        rows.Setup(r => r.GetLiveAsync(Owner, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Passkey(laptop, "Laptop", Now.AddDays(-2)), Passkey(phone, "Phone", Now.AddDays(-1))]);
        rows.Setup(r => r.GetUserHandleAsync(Owner, It.IsAny<CancellationToken>())).ReturnsAsync(handle);

        var result = await new GetMyPasskeysQueryHandler(PasskeyTestDoubles.Feature(), rows.Object)
            .Handle(new GetMyPasskeysQuery(Owner), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RpId.Should().Be(PasskeyTestDoubles.RpId);
        result.Value.UserHandle.Should().Be(Base64Url.EncodeToString(handle));
        result.Value.UserHandle.Should().NotContainAny("+", "/", "=");
        result.Value.Passkeys.Select(p => p.Name).Should().Equal("Laptop", "Phone");
        result.Value.Passkeys.Select(p => p.CredentialId).Should().Equal("-_8BAg", "AD4_QEE");
    }

    [Fact]
    public async Task BeforeTheFirstPasskey_TheHandleIsNull_AndTheListEmpty()
    {
        var rows = PasskeyTestDoubles.EmptyRepository();
        rows.Setup(r => r.GetUserHandleAsync(Owner, It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

        var result = await new GetMyPasskeysQueryHandler(PasskeyTestDoubles.Feature(), rows.Object)
            .Handle(new GetMyPasskeysQuery(Owner), CancellationToken.None);

        result.Value.UserHandle.Should().BeNull();
        result.Value.Passkeys.Should().BeEmpty();
        result.Value.RpId.Should().Be(PasskeyTestDoubles.RpId);
    }

    [Fact]
    public async Task AfterTheLastPasskeyWasRemoved_TheHandleIsStillAnswered()
    {
        // D-S20-7: an empty list is what the page tells the browser then, so the handle a
        // browser needs to apply it must not depend on a passkey being left.
        var rows = PasskeyTestDoubles.EmptyRepository();
        rows.Setup(r => r.GetUserHandleAsync(Owner, It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);

        var result = await new GetMyPasskeysQueryHandler(PasskeyTestDoubles.Feature(), rows.Object)
            .Handle(new GetMyPasskeysQuery(Owner), CancellationToken.None);

        result.Value.Passkeys.Should().BeEmpty();
        result.Value.UserHandle.Should().Be("AQID");
    }

    [Fact]
    public async Task TheController_AnswersTheListAsTheHandlerBuiltIt_ForTheSignedInOwner()
    {
        var list = new PasskeyListDto(
            PasskeyTestDoubles.RpId,
            "AQID",
            [new PasskeyDto(Guid.NewGuid(), "Laptop", Now, null, true, true, null, "-_8BAg")]);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.Is<GetMyPasskeysQuery>(q => q.UserId == Owner), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<PasskeyListDto>)list);
        var controller = new PasskeysController(sender.Object, Snapshot(new IdentityProviderSettings()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Owner.ToString())], "test")),
                },
            },
        };

        var answer = await controller.List(CancellationToken.None);

        answer.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(list);
    }

    [Fact]
    public async Task TheController_AnswersNoOneWithoutASignedInOwner()
    {
        var sender = new Mock<ISender>();
        var controller = new PasskeysController(sender.Object, Snapshot(new IdentityProviderSettings()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        (await controller.List(CancellationToken.None)).Should().BeOfType<UnauthorizedResult>();
        sender.Verify(s => s.Send(It.IsAny<GetMyPasskeysQuery>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static IOptionsSnapshot<T> Snapshot<T>(T value)
        where T : class
    {
        var snapshot = new Mock<IOptionsSnapshot<T>>();
        snapshot.Setup(s => s.Value).Returns(value);
        return snapshot.Object;
    }
}
