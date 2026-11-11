using System.Reflection;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.GoogleClientSecret;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.Administration.Controllers;
using Auth_API.Modules.Administration.Filters;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S02 PR D: Google's client secret is a secret, provisioned like Apple's key. Set and cleared
/// from the console's Secrets page into the encrypted file; read into
/// <c>ExternalAuth:Google:ClientSecret</c> at startup; never writable through the system-settings
/// layer; refused in plaintext Production configuration.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the provider's mapping line removed; the status entry
/// removed; the handler writing in PlainText mode; the key left out of SecretOwnedKeys; the key
/// left out of ProductionSecretGuard; <c>[RequirePermission]</c> removed from the PUT.
/// </remarks>
public sealed class GoogleClientSecretTests : IDisposable
{
    private const string Secret = "GOCSPX-stored-google-client-secret";

    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-google-secret", Guid.NewGuid().ToString("N"));

    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _provider;
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Guid _admin = Guid.NewGuid();

    public GoogleClientSecretTests()
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

    private GoogleClientSecretCommandHandler CreateHandler(IDpapiSecretService service, string storageMode = "Dpapi") =>
        new(
            service,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode, SecretFilePath = _secretFilePath }),
            _publisher.Object,
            NullLogger<GoogleClientSecretCommandHandler>.Instance);

    /// <summary>The real layer order: the settings file first, the secrets file last.</summary>
    private IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAuth:Google:ClientId"] = "test-client" })
            .AddDpapiSecrets(_provider, _secretFilePath)
            .Build();

    [Fact]
    public async Task Set_StoresTheSecret_WhichStartupReadsAsTheGoogleClientSecret_AndTheStatusReportsIt()
    {
        var service = await CreateServiceAsync();

        var result = await CreateHandler(service).Handle(new SetGoogleClientSecretCommand(Secret, _admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        Configuration()["ExternalAuth:Google:ClientSecret"].Should().Be(Secret);
        var status = await service.GetStatusAsync(CancellationToken.None);
        status.Secrets["GoogleClientSecret"].Should().Be(SecretStatus.Configured,
            "the Secrets page shows whether it is set, never the value");
    }

    [Fact]
    public async Task Set_TrimsWhatWasPasted()
    {
        var service = await CreateServiceAsync();

        await CreateHandler(service).Handle(new SetGoogleClientSecretCommand($"  {Secret}\r\n", _admin), CancellationToken.None);

        Configuration()["ExternalAuth:Google:ClientSecret"].Should().Be(Secret,
            "a stray space or line break would be refused by Google only after the restart");
    }

    [Fact]
    public async Task TheSecret_IsWriteOnly_ThroughTheSecretService()
    {
        var service = await CreateServiceAsync();
        await CreateHandler(service).Handle(new SetGoogleClientSecretCommand(Secret, _admin), CancellationToken.None);

        (await service.GetSecretAsync("GoogleClientSecret", CancellationToken.None)).Should().BeNull(
            "no key name reads it back; only its status is reported");
    }

    [Fact]
    public async Task Clear_RemovesTheSecret_FromTheFileAndFromConfiguration()
    {
        var service = await CreateServiceAsync();
        var handler = CreateHandler(service);
        await handler.Handle(new SetGoogleClientSecretCommand(Secret, _admin), CancellationToken.None);

        var result = await handler.Handle(new ClearGoogleClientSecretCommand(_admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        Configuration()["ExternalAuth:Google:ClientSecret"].Should().BeNull();
        (await service.GetStatusAsync(CancellationToken.None)).Secrets["GoogleClientSecret"]
            .Should().Be(SecretStatus.NotConfigured);
    }

    [Fact]
    public async Task StatusWithoutAFile_ListsTheGoogleClientSecret_AsNotConfigured()
    {
        var service = new DpapiSecretService(
            _provider,
            Options.Create(new SecretManagementSettings { SecretFilePath = _secretFilePath }),
            NullLogger<DpapiSecretService>.Instance);

        (await service.GetStatusAsync(CancellationToken.None)).Secrets["GoogleClientSecret"]
            .Should().Be(SecretStatus.NotConfigured, "the row is there to set it from, before any file exists");
    }

    [Fact]
    public async Task SetAndClear_InPlainTextMode_AreRefused_AndWriteNothing()
    {
        var service = new Mock<IDpapiSecretService>(MockBehavior.Strict);
        var handler = CreateHandler(service.Object, storageMode: "PlainText");

        (await handler.Handle(new SetGoogleClientSecretCommand(Secret, _admin), CancellationToken.None))
            .FirstError.Should().Be(SecretErrors.SetNotSupportedInPlainText);
        (await handler.Handle(new ClearGoogleClientSecretCommand(_admin), CancellationToken.None))
            .FirstError.Should().Be(SecretErrors.SetNotSupportedInPlainText);
    }

    [Fact]
    public async Task SetAndClear_AreAudited_ByTheKeyName_NeverTheValue()
    {
        var service = await CreateServiceAsync();
        var handler = CreateHandler(service);

        await handler.Handle(new SetGoogleClientSecretCommand(Secret, _admin), CancellationToken.None);
        await handler.Handle(new ClearGoogleClientSecretCommand(_admin), CancellationToken.None);

        _publisher.Verify(
            publisher => publisher.Publish(
                It.Is<SecretValueChangedEvent>(e => e.SecretKey == "GoogleClientSecret" && e.ChangedBy == _admin),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData("", "Secret.ValueRequired")]
    [InlineData(null, "Secret.ValueRequired")]
    public void Validator_RefusesAnEmptyValue(string? value, string code)
    {
        var result = new SetGoogleClientSecretCommandValidator().Validate(new SetGoogleClientSecretCommand(value!, _admin));

        result.Errors.Select(error => error.ErrorCode).Should().Equal(code);
    }

    [Fact]
    public void Validator_RefusesMoreThan512Characters_AndAccepts512()
    {
        var validator = new SetGoogleClientSecretCommandValidator();

        validator.Validate(new SetGoogleClientSecretCommand(new string('s', 512), _admin)).IsValid.Should().BeTrue();
        validator.Validate(new SetGoogleClientSecretCommand(new string('s', 513), _admin)).Errors
            .Select(error => error.ErrorCode).Should().Equal("Secret.ValueTooLong");
    }

    [Fact]
    public void TheSettingsLayer_CanNeitherReadNorWriteIt()
    {
        SecretOwnedKeys.IsSecretOwned("ExternalAuth:Google:ClientSecret").Should().BeTrue(
            "a database override would put the secret in plaintext beside the client id");
        SecretOwnedKeys.IsSecretOwned("ExternalAuth:Google:ClientId").Should().BeFalse("the client id stays console-editable");

        var section = SystemSettingsRegistry.TryGet("ExternalAuth");
        SystemSettingsRegistry.TryGetField(section!, "Google:ClientSecret")!.Sensitive.Should().BeTrue(
            "System settings shows it as a link to the Secrets page, like Apple's key");
    }

    [Fact]
    public void Production_RefusesItInPlaintextConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAuth:Google:ClientSecret"] = Secret })
            .Build();

        var act = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(configuration, isProduction: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ExternalAuth:Google:ClientSecret*");
        var development = () => ProductionSecretGuard.EnsureNoPlaintextSecrets(configuration, isProduction: false);
        development.Should().NotThrow("Development keeps it in the git-ignored local settings file");
    }

    [Theory]
    [InlineData(nameof(SecretsController.SetGoogleClientSecret), "PUT")]
    [InlineData(nameof(SecretsController.ClearGoogleClientSecret), "DELETE")]
    public void TheConsoleEndpoints_NeedSecretsManage_BehindTheAdminApiSwitch(string action, string verb)
    {
        var method = typeof(SecretsController).GetMethod(action)!;

        method.GetCustomAttribute<RequirePermissionAttribute>()!.Permission.Should().Be(PermissionCodes.Secrets.Manage);
        method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
            .Should().ContainSingle(attribute => attribute.HttpMethods.Contains(verb) && attribute.Template == "google-client-secret");
        typeof(SecretsController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(SecretsController).GetCustomAttribute<RequireAdminApiEnabledAttribute>().Should().NotBeNull();
    }
}
