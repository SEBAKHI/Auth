using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.TurnstileSecret;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS7 (BD8, ARR-0412): Cloudflare Turnstile's secret is set and cleared from the console's
/// Secrets page into the encrypted file, read into <c>BotDefense:TurnstileSecret</c> at startup,
/// reported by the status (never by value), audited by name; refused in PlainText storage, and in
/// Production refused when it is one of Cloudflare's testing secrets.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the PlainText check removed; the testing-secret check
/// removed; the provider's mapping line removed; the status entry removed.
/// </remarks>
public sealed class SetTurnstileSecretCommandHandlerTests : IDisposable
{
    private const string Secret = "0x4AAAAAAAturnstile-secret-for-tests";

    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-turnstile-secret", Guid.NewGuid().ToString("N"));

    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _provider;
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Guid _admin = Guid.NewGuid();

    public SetTurnstileSecretCommandHandlerTests()
    {
        Directory.CreateDirectory(_workingDirectory);
        _secretFilePath = Path.Combine(_workingDirectory, "secrets.dpapi");

        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("AuthSystem")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_workingDirectory, "keys")));
        _provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }

    private async Task<DpapiSecretService> CreateServiceAsync()
    {
        var service = new DpapiSecretService(
            _provider,
            Options.Create(new SecretManagementSettings { SecretFilePath = _secretFilePath }),
            NullLogger<DpapiSecretService>.Instance);
        await service.GenerateMissingKeysAsync(CancellationToken.None);
        return service;
    }

    private TurnstileSecretCommandHandler CreateHandler(
        Auth.Application.Interfaces.IDpapiSecretService service, string storageMode = "Dpapi", bool production = false) =>
        new(
            service,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode, SecretFilePath = _secretFilePath }),
            new FakeEnvironment(production),
            _publisher.Object,
            NullLogger<TurnstileSecretCommandHandler>.Instance);

    private IConfiguration Configuration() =>
        new ConfigurationBuilder().AddDpapiSecrets(_provider, _secretFilePath).Build();

    [Fact]
    public async Task Set_StoresTheSecret_WhichStartupReadsAsBotDefenseTurnstileSecret_AndTheStatusReportsIt()
    {
        var service = await CreateServiceAsync();

        var result = await CreateHandler(service).Handle(new SetTurnstileSecretCommand($"  {Secret}\r\n", _admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        Configuration()["BotDefense:TurnstileSecret"].Should().Be(Secret, "trimmed, and mapped where the check reads it");
        (await service.GetStatusAsync(CancellationToken.None)).Secrets["TurnstileSecretKey"]
            .Should().Be(SecretStatus.Configured);
        _publisher.Verify(
            p => p.Publish(
                It.Is<SecretValueChangedEvent>(e => e.SecretKey == "TurnstileSecretKey" && e.ChangedBy == _admin),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StatusWithoutAFile_ListsTheTurnstileSecret_AsNotConfigured()
    {
        var service = new DpapiSecretService(
            _provider,
            Options.Create(new SecretManagementSettings { SecretFilePath = _secretFilePath }),
            NullLogger<DpapiSecretService>.Instance);

        (await service.GetStatusAsync(CancellationToken.None)).Secrets["TurnstileSecretKey"]
            .Should().Be(SecretStatus.NotConfigured);
    }

    [Fact]
    public async Task Clear_RemovesIt()
    {
        var service = await CreateServiceAsync();
        var handler = CreateHandler(service);
        await handler.Handle(new SetTurnstileSecretCommand(Secret, _admin), CancellationToken.None);

        (await handler.Handle(new ClearTurnstileSecretCommand(_admin), CancellationToken.None)).IsError.Should().BeFalse();

        Configuration()["BotDefense:TurnstileSecret"].Should().BeNull();
        (await service.GetStatusAsync(CancellationToken.None)).Secrets["TurnstileSecretKey"]
            .Should().Be(SecretStatus.NotConfigured);
    }

    [Fact]
    public async Task PlainTextStorage_IsRefused_AndWritesNothing()
    {
        var service = await CreateServiceAsync();

        var result = await CreateHandler(service, storageMode: "PlainText")
            .Handle(new SetTurnstileSecretCommand(Secret, _admin), CancellationToken.None);

        result.FirstError.Code.Should().Be(SecretErrors.SetNotSupportedInPlainText.Code);
        Configuration()["BotDefense:TurnstileSecret"].Should().BeNull();
        _publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ATestingSecret_IsRefusedInProduction_AndAcceptedElsewhere()
    {
        var service = await CreateServiceAsync();
        const string testing = "1x0000000000000000000000000000000AA";

        var production = await CreateHandler(service, production: true)
            .Handle(new SetTurnstileSecretCommand(testing, _admin), CancellationToken.None);
        production.FirstError.Code.Should().Be(BotDefenseErrors.TestingKeyInProduction.Code);
        Configuration()["BotDefense:TurnstileSecret"].Should().BeNull();

        var development = await CreateHandler(service, production: false)
            .Handle(new SetTurnstileSecretCommand(testing, _admin), CancellationToken.None);
        development.IsError.Should().BeFalse();
        Configuration()["BotDefense:TurnstileSecret"].Should().Be(testing);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TheValidator_RefusesAnEmptyValue(string? value)
    {
        new SetTurnstileSecretCommandValidator().Validate(new SetTurnstileSecretCommand(value!, _admin))
            .Errors.Should().ContainSingle(error => error.ErrorCode == SecretErrors.ValueRequired.Code);
    }

    [Fact]
    public void TheCommand_NeverPrintsTheValue()
    {
        new SetTurnstileSecretCommand(Secret, _admin).ToString().Should().NotContain(Secret);
    }
}
