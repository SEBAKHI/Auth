using Auth.Application.Configuration;
using Auth.Application.Features.Platform.GetPasswordPolicy;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Administration;

public class GetPasswordPolicyQueryHandlerTests
{
    private static GetPasswordPolicyQueryHandler CreateHandler(PasswordSettings settings)
        => new(TestHelpers.CreateOptions(settings));

    [Fact]
    public async Task Handle_ReturnsTheConfiguredCompositionRules()
    {
        var handler = CreateHandler(new PasswordSettings
        {
            MinimumLength = 12,
            RequireUppercase = true,
            RequireLowercase = false,
            RequireDigit = true,
            RequireSpecialCharacter = false
        });

        var result = await handler.Handle(new GetPasswordPolicyQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.MinimumLength.Should().Be(12);
        result.Value.RequireUppercase.Should().BeTrue();
        result.Value.RequireLowercase.Should().BeFalse();
        result.Value.RequireDigit.Should().BeTrue();
        result.Value.RequireSpecialCharacter.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReflectsAMinimumLoweredToTheRegistryFloor()
    {
        // An operator who lowers the policy to 6 must see 6 on the form as well;
        // the whole point of the endpoint is that no client keeps its own number.
        var handler = CreateHandler(new PasswordSettings { MinimumLength = 6 });

        var result = await handler.Handle(new GetPasswordPolicyQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.MinimumLength.Should().Be(6);
    }
}
