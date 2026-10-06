using System.Collections;
using System.Reflection;
using Auth.Application.Validators.Rules;
using Auth.Domain.Entities;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Tests.Helpers;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.OidcUserInfo;

/// <summary>
/// The one builder of the API's token-validation parameters (X11). The userinfo profile must
/// accept an application's token and nothing else; the platform profile must stay, field by
/// field, what Program.cs set before the builder existed. Every token here is real: minted by
/// the token service or signed with its key, and validated by the handler JwtBearer runs.
/// </summary>
public sealed class AccessTokenValidationTests : IDisposable
{
    private readonly UserInfoTokens _tokens = new();
    private readonly User _user = TestHelpers.CreateUser();

    public void Dispose() => _tokens.Dispose();

    private async Task<bool> PassesUserInfoAsync(string token) =>
        (await UserInfoTokens.ValidateAsync(token, _tokens.UserInfoParameters)).IsValid;

    // --- UserInfo(): what it accepts ---

    [Fact]
    public async Task UserInfo_ApplicationToken_Passes()
    {
        var token = _tokens.ForApplication(_user, "openid profile");

        (await PassesUserInfoAsync(token)).Should().BeTrue();
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("at+jwt")]
    [InlineData("application/at+jwt")]
    public async Task UserInfo_EveryApplicationTokenType_Passes(string type)
    {
        var token = _tokens.Custom(_user.Id, descriptor => descriptor.TokenType = type);

        (await PassesUserInfoAsync(token)).Should().BeTrue();
    }

    // --- UserInfo(): what it refuses ---

    [Fact]
    public async Task UserInfo_PlatformTokenWithACodeShapedAudience_IsRefusedByTheOrdinalCheck()
    {
        // Jwt:Audience here is "authsystem-api", which passes the code rule: only the comparison
        // with the platform audience can refuse this token.
        var token = _tokens.ForPlatform(_user);

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    [Fact]
    public async Task UserInfo_PlatformTokenWithAUrlAudience_IsRefused()
    {
        using var tokens = new UserInfoTokens(UserInfoTokens.UrlPlatformAudience);
        var token = tokens.ForPlatform(_user);

        (await UserInfoTokens.ValidateAsync(token, tokens.UserInfoParameters)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task UserInfo_TwoApplicationAudiences_IsRefused()
    {
        // Replayable at the second application: one audience or none.
        var token = _tokens.Custom(_user.Id, descriptor =>
        {
            descriptor.Audience = null;
            descriptor.Claims["aud"] = new List<string> { UserInfoTokens.Application, "other-app" };
        });

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    public static TheoryData<string> NotAnApplicationCode => new()
    {
        "edis app",
        "https://x",
        "edis/",
        new string('a', 101),
        // In .NET "$" matches before a final newline; the rule must not.
        "edis\n",
        // Letters and digits outside ASCII: "ÉDIS", and "edis" with ARABIC-INDIC DIGIT ONE.
        "ÉDIS",
        "edis١",
    };

    [Fact]
    public void NotAnApplicationCode_IsNotEmpty() =>
        NotAnApplicationCode.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(NotAnApplicationCode))]
    public async Task UserInfo_AudienceOutsideTheCodeRule_IsRefused(string audience)
    {
        var token = _tokens.Custom(_user.Id, descriptor => descriptor.Audience = audience);

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    [Fact]
    public void CodeRule_TrailingNewline_IsRefusedByTheCodeValidatorToo()
    {
        // One pattern for both: an application whose Code the validator accepts must be one whose
        // token the userinfo scheme accepts, and the reverse.
        var validator = new InlineValidator<CodeHolder>();
        validator.RuleFor(x => x.Code).IsValidCode();

        validator.Validate(new CodeHolder("edis\n")).IsValid.Should().BeFalse();
        validator.Validate(new CodeHolder("edis")).IsValid.Should().BeTrue();
    }

    private sealed record CodeHolder(string Code);

    [Fact]
    public async Task UserInfo_LongestCodeTheRuleAllows_Passes()
    {
        // The boundary of the refusal above: 100 characters is still a code.
        var token = _tokens.Custom(_user.Id, descriptor => descriptor.Audience = new string('a', 100));

        (await PassesUserInfoAsync(token)).Should().BeTrue();
    }

    [Fact]
    public async Task UserInfo_AnotherAlgorithmUnderTheSameKey_IsRefused()
    {
        var token = _tokens.Custom(_user.Id, descriptor =>
            descriptor.SigningCredentials = new SigningCredentials(_tokens.Key, SecurityAlgorithms.RsaSha512));

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    [Fact]
    public async Task UserInfo_ExpiredToken_IsRefusedAsExpired()
    {
        var token = _tokens.Custom(_user.Id, descriptor =>
        {
            descriptor.IssuedAt = DateTime.UtcNow.AddMinutes(-30);
            descriptor.NotBefore = DateTime.UtcNow.AddMinutes(-30);
            descriptor.Expires = DateTime.UtcNow.AddMinutes(-10);
        });

        var result = await UserInfoTokens.ValidateAsync(token, _tokens.UserInfoParameters);

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenExpiredException>();
    }

    [Fact]
    public async Task UserInfo_AnotherIssuer_IsRefused()
    {
        var token = _tokens.Custom(_user.Id, descriptor => descriptor.Issuer = "https://evil.example.com");

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    [Fact]
    public async Task UserInfo_IdTokenType_IsRefused()
    {
        var token = _tokens.Custom(_user.Id, descriptor => descriptor.TokenType = "id+jwt");

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    [Fact]
    public async Task UserInfo_AnotherSigningKey_IsRefused()
    {
        using var stranger = new UserInfoTokens();
        var token = stranger.ForApplication(_user, "openid");

        (await PassesUserInfoAsync(token)).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_ApplicationToken_IsRefused_AndPlatformTokenPasses()
    {
        // The other half of the isolation: the default scheme keeps refusing what userinfo takes.
        (await UserInfoTokens.ValidateAsync(_tokens.ForApplication(_user, "openid"), _tokens.PlatformParameters))
            .IsValid.Should().BeFalse();
        (await UserInfoTokens.ValidateAsync(_tokens.ForPlatform(_user), _tokens.PlatformParameters))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void UserInfo_TokenTypes_AreTheApplicationTypesExactly()
    {
        _tokens.UserInfoParameters.ValidTypes.Should().Equal("JWT", "at+jwt", "application/at+jwt");
        AccessTokenValidation.AppAccessTokenTypes.Should().Equal("JWT", "at+jwt", "application/at+jwt");
    }

    // --- Platform(): parity with what Program.cs set before the builder (b2189dfa, :793-807) ---

    /// <summary>The literal Program.cs held, copied here so a change to the builder shows.</summary>
    private TokenValidationParameters ProgramLiteralBeforeTheBuilder() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _tokens.Settings.Issuer,
        ValidateAudience = true,
        ValidAudience = _tokens.Settings.Audience,
        ValidateLifetime = true,
        ClockSkew = _tokens.Settings.ClockSkew,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = ["RS256"],
        IssuerSigningKey = _tokens.Key
    };

    [Fact]
    public void Platform_EqualsTheProgramLiteral_FieldByField()
    {
        AssertSameParameters(_tokens.PlatformParameters, ProgramLiteralBeforeTheBuilder());
    }

    [Fact]
    public void UserInfo_DiffersFromPlatform_OnlyInTheAudienceRuleAndTheTypes()
    {
        var userInfo = _tokens.UserInfoParameters;
        var platform = _tokens.PlatformParameters;

        userInfo.ValidAudience.Should().BeNull();
        userInfo.AudienceValidator.Should().NotBeNull();
        platform.AudienceValidator.Should().BeNull();
        platform.ValidTypes.Should().BeNull();

        // Everything else, key and issuer included, is the same: the drift a second literal had.
        platform.ValidAudience = null;
        platform.AudienceValidator = userInfo.AudienceValidator;
        platform.ValidTypes = userInfo.ValidTypes;
        AssertSameParameters(userInfo, platform);
    }

    // --- The registration Program.cs calls ---

    [Fact]
    public void Registration_DefaultSchemeIsPlatform_AndTheUserInfoSchemeIsTheUserInfoProfile()
    {
        using var provider = RegisteredSchemes();
        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        authentication.DefaultAuthenticateScheme.Should().Be(JwtBearerDefaults.AuthenticationScheme);
        authentication.DefaultChallengeScheme.Should().Be(JwtBearerDefaults.AuthenticationScheme);
        authentication.DefaultScheme.Should().BeNull();
        authentication.Schemes.Select(s => s.Name).Should().BeEquivalentTo(
            JwtBearerDefaults.AuthenticationScheme, AccessTokenValidation.UserInfoScheme);

        var platform = bearer.Get(JwtBearerDefaults.AuthenticationScheme);
        AssertSameParameters(platform.TokenValidationParameters, ProgramLiteralBeforeTheBuilder());

        var userInfo = bearer.Get(AccessTokenValidation.UserInfoScheme);
        AssertSameParameters(userInfo.TokenValidationParameters, _tokens.UserInfoParameters);

        foreach (var options in new[] { platform, userInfo })
        {
            options.MapInboundClaims.Should().BeFalse();
            options.Authority.Should().BeNull();
            options.MetadataAddress.Should().BeNullOrEmpty();
            options.Events.OnChallenge.Should().Be((Func<JwtBearerChallengeContext, Task>)JwtChallengeReasons.Record);
            // Not a subclass: an override of MessageReceived could read the token from the query.
            options.Events.GetType().Should().Be<JwtBearerEvents>();
            options.EventsType.Should().BeNull();
        }
    }

    private ServiceProvider RegisteredSchemes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Every public property of the two parameter sets, compared by value; delegates only by
    /// presence, since two builds of the same closure are different instances.
    /// </summary>
    private static void AssertSameParameters(TokenValidationParameters actual, TokenValidationParameters expected)
    {
        var properties = typeof(TokenValidationParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        properties.Should().NotBeEmpty();

        foreach (var property in properties)
        {
            var a = property.GetValue(actual);
            var e = property.GetValue(expected);

            if (typeof(Delegate).IsAssignableFrom(property.PropertyType))
            {
                (a is null).Should().Be(e is null, "{0} must be set on both or on neither", property.Name);
            }
            else if (a is IEnumerable actualItems and not string && e is IEnumerable expectedItems)
            {
                actualItems.Cast<object>().Should().Equal(expectedItems.Cast<object>(), "{0} must match", property.Name);
            }
            else
            {
                a.Should().Be(e, "{0} must match", property.Name);
            }
        }
    }
}
