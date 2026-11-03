using System.Text;
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// OI-217 part 1: one writer at a time on the secrets file, across API processes, and the boot
/// steps keep what another process wrote. Two <see cref="DpapiSecretService"/> instances on one
/// path stand for two processes: each has its own semaphore, so only the lock file can keep them
/// apart, and the lock is an open handle that shares nothing, which the system grants to one
/// holder whether the holders are two processes or two instances of one.
/// </summary>
public sealed class SecretFileLockTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authsystem-secret-lock", Guid.NewGuid().ToString("N"));
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    public SecretFileLockTests() => Directory.CreateDirectory(_directory);

    private string FilePath => Path.Combine(_directory, "secrets.dpapi");

    private string LockPath => FilePath + ".lock";

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    // One API process's view of the file.
    private DpapiSecretService Process(TimeSpan? lockTimeout = null) => new(
        _protection,
        Options.Create(new SecretManagementSettings { SecretFilePath = FilePath, AutoGenerateKeys = true }),
        NullLogger<DpapiSecretService>.Instance)
    {
        FileLockTimeout = lockTimeout ?? Bound
    };

    private static Func<SecretConfiguration, ErrorOr<Success>> SetCustom(string key, string value) =>
        secrets =>
        {
            secrets.Custom[key] = value;
            return Result.Success;
        };

    [Fact]
    public async Task ASecondProcess_ReadsTheFileOnlyOnceTheFirstHasWrittenIt_AndBothChangesSurvive()
    {
        var a = Process();
        var b = Process();
        await a.UpdateAsync(SetCustom("seed", "0"), overwriteBackup: false, CancellationToken.None);

        using var aIsChanging = new ManualResetEventSlim();
        using var releaseA = new ManualResetEventSlim();
        var bSaw = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(() => a.UpdateAsync<Success>(
            secrets =>
            {
                aIsChanging.Set();
                releaseA.Wait(Bound);
                secrets.Custom["a"] = "written by A";
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None));
        aIsChanging.Wait(Bound).Should().BeTrue();

        var second = Task.Run(() => b.UpdateAsync<Success>(
            secrets =>
            {
                bSaw.TrySetResult(secrets.Custom.GetValueOrDefault("a"));
                secrets.Custom["b"] = "written by B";
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None));

        (await Task.WhenAny(bSaw.Task, Task.Delay(TimeSpan.FromMilliseconds(750))))
            .Should().NotBeSameAs(bSaw.Task, "B must not read the file while A is changing it");

        releaseA.Set();
        (await first).IsError.Should().BeFalse();
        (await second).IsError.Should().BeFalse();

        (await bSaw.Task).Should().Be("written by A", "B read the file A had written");
        var file = await Process().LoadSecretsAsync(CancellationToken.None);
        file.Custom.Should().Contain("a", "written by A").And.Contain("b", "written by B");
    }

    [Fact]
    public async Task TwoProcessesStartingOnAMissingFile_WriteItOnce_AndEndOnTheSameKeys()
    {
        var a = Process();
        var b = Process();
        using var aIsGenerating = new ManualResetEventSlim();
        using var releaseA = new ManualResetEventSlim();
        var bWouldWrite = false;

        // A has generated its keys and holds the lock, about to write the new file.
        var first = Task.Run(() => a.GenerateMissingKeysAsync(
            CancellationToken.None,
            beforeSave: _ =>
            {
                aIsGenerating.Set();
                releaseA.Wait(Bound);
            }));
        aIsGenerating.Wait(Bound).Should().BeTrue();

        var second = Task.Run(() => b.GenerateMissingKeysAsync(CancellationToken.None, beforeSave: _ => bWouldWrite = true));
        await Task.Delay(TimeSpan.FromMilliseconds(750));
        File.Exists(FilePath).Should().BeFalse("B waits for A's write instead of writing a file of its own");

        releaseA.Set();
        var generatedByA = await first;
        var generatedByB = await second;

        generatedByA.GeneratedKeys.Should().NotBeEmpty();
        generatedByB.GeneratedKeys.Should().BeEmpty("B found every key in the file A wrote");
        bWouldWrite.Should().BeFalse("B never reached a write");
        var seenByA = await a.LoadSecretsAsync(CancellationToken.None);
        var seenByB = await b.LoadSecretsAsync(CancellationToken.None);
        seenByB.JwtPrivateKeyPem.Should().Be(seenByA.JwtPrivateKeyPem);
        seenByB.RefreshTokenHmacKey.Should().Be(seenByA.RefreshTokenHmacKey);
        seenByB.GatewayToken.Should().Be(seenByA.GatewayToken);
        seenByB.AccountDeletionIdentifierHmacKey.Should().Be(seenByA.AccountDeletionIdentifierHmacKey);
    }

    [Fact]
    public async Task AFileCreatedAfterTheRead_ByAWriterThatTakesNoLock_IsNeverWrittenOver()
    {
        var service = Process();
        var foreign = Encoding.UTF8.GetBytes("the file a process without the lock wrote");

        var act = () => service.UpdateAsync<Success>(
            secrets =>
            {
                // The file was missing when this change read it. A process that takes no lock
                // (an older build, during a deploy) creates it now.
                File.WriteAllBytes(FilePath, foreign);
                secrets.Custom["mine"] = "x";
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        (await act.Should().ThrowAsync<IOException>()).WithMessage("*created the secrets file*");
        File.ReadAllBytes(FilePath).Should().Equal(foreign, "nothing was written over it");
    }

    [Fact]
    public async Task AFirstStart_RefusesToWriteOverAFileCreatedWhileItGenerated_ByAWriterThatTakesNoLock()
    {
        var service = Process();
        var foreign = Encoding.UTF8.GetBytes("the file a process without the lock wrote");

        var act = () => service.GenerateMissingKeysAsync(
            CancellationToken.None,
            beforeSave: _ => File.WriteAllBytes(FilePath, foreign));

        (await act.Should().ThrowAsync<IOException>()).WithMessage("*Nothing was written*");
        File.ReadAllBytes(FilePath).Should().Equal(foreign);
    }

    [Fact]
    public async Task TheLockFile_StaysAfterEveryWrite_AndHoldsNothing()
    {
        var service = Process();

        await service.UpdateAsync(SetCustom("first", "1"), overwriteBackup: false, CancellationToken.None);
        File.Exists(LockPath).Should().BeTrue("deleting it would let the next two writers each lock a different file");
        await service.UpdateAsync(SetCustom("second", "2"), overwriteBackup: false, CancellationToken.None);

        File.Exists(LockPath).Should().BeTrue();
        new FileInfo(LockPath).Length.Should().Be(0, "the lock file holds nothing");
    }

    [Fact]
    public async Task AHolderThatDied_BlocksNoOne()
    {
        var service = Process(lockTimeout: TimeSpan.FromMilliseconds(500));

        // A process that dies holding the lock: the system closes its handle, and the file stays.
        using (new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
        }

        var write = service.UpdateAsync(SetCustom("after", "1"), overwriteBackup: false, CancellationToken.None);
        (await Task.WhenAny(write, Task.Delay(Bound))).Should().BeSameAs(write);
        (await write).IsError.Should().BeFalse("the handle is the lock, not the file's existence");

        (await service.LoadSecretsAsync(CancellationToken.None)).Custom.Should().ContainKey("after");
    }

    [Fact]
    public async Task ALockHeldPastTheWait_RefusesTheWrite_NamingTheLockFile_AndWritesNothing()
    {
        var service = Process(lockTimeout: TimeSpan.FromMilliseconds(300));
        await service.UpdateAsync(SetCustom("before", "1"), overwriteBackup: false, CancellationToken.None);
        var before = File.ReadAllBytes(FilePath);

        using (new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var write = service.UpdateAsync(SetCustom("after", "2"), overwriteBackup: false, CancellationToken.None);

            (await Task.WhenAny(write, Task.Delay(Bound))).Should().BeSameAs(write, "the wait is bounded");
            var refused = await FluentActions.Awaiting(() => write).Should().ThrowAsync<IOException>();
            refused.Which.Message.Should().Contain(LockPath).And.Contain("Nothing was written");
        }

        File.ReadAllBytes(FilePath).Should().Equal(before);
    }

    [Fact]
    public async Task ALoad_TakesNoLockAcrossProcesses()
    {
        var service = Process(lockTimeout: TimeSpan.FromMilliseconds(300));
        await service.UpdateAsync(SetCustom("kept", "1"), overwriteBackup: false, CancellationToken.None);

        using (new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            (await Process().LoadSecretsAsync(CancellationToken.None)).Custom.Should().ContainKey("kept",
                "readers never wait for a writer: every write replaces the file in one step");
        }
    }

    [Fact]
    public async Task TheIdentifierKeyTopUp_KeepsAKeyAnotherProcessWroteFirst_AndWritesNothing()
    {
        var other = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();
        await Process().UpdateAsync<Success>(
            secrets =>
            {
                secrets.AccountDeletionIdentifierHmacKey = other;
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);
        var before = File.ReadAllBytes(FilePath);

        var wrote = await Process().ProvisionIdentifierKeyAsync(Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64(), CancellationToken.None);

        wrote.Should().BeFalse();
        File.ReadAllBytes(FilePath).Should().Equal(before, "the file is not written at all");
        LayeredFromTheFile()["AccountDeletion:IdentifierHmacKeyPlain"].Should().Be(other,
            "the process re-layers from the file, so it ends on the key the other process wrote");
    }

    [Fact]
    public async Task TheIdentifierKeyTopUp_WritesItsKey_WhileTheFileHasNone()
    {
        await Process().UpdateAsync(SetCustom("present", "1"), overwriteBackup: false, CancellationToken.None);
        var minted = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();

        (await Process().ProvisionIdentifierKeyAsync(minted, CancellationToken.None)).Should().BeTrue();

        LayeredFromTheFile()["AccountDeletion:IdentifierHmacKeyPlain"].Should().Be(minted);
    }

    [Fact]
    public async Task ThePepperStep_KeepsAPepperAnotherProcessWroteFirst_AndWritesNothing()
    {
        var other = Auth.Shared.Configuration.KeyMaterialGenerator.GeneratePepperBase64();
        await Process().UpdateAsync<Success>(
            secrets =>
            {
                secrets.PasswordPeppers[1] = other;
                secrets.PasswordPepperCurrentKeyId = 1;
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);
        var before = File.ReadAllBytes(FilePath);

        var wrote = await Process().ProvisionPasswordPepperAsync(Auth.Shared.Configuration.KeyMaterialGenerator.GeneratePepperBase64(), CancellationToken.None);

        wrote.Should().BeFalse();
        File.ReadAllBytes(FilePath).Should().Equal(before, "the file is not written at all");
        var configuration = LayeredFromTheFile();
        configuration["Password:Pepper:Keys:1"].Should().Be(other, "the process ends on the pepper the file holds");
        configuration["Password:Pepper:CurrentKeyId"].Should().Be("1");
    }

    [Fact]
    public async Task ThePepperStep_WritesItsPepper_WhileTheFileNamesNone()
    {
        await Process().UpdateAsync(SetCustom("present", "1"), overwriteBackup: false, CancellationToken.None);
        var minted = Auth.Shared.Configuration.KeyMaterialGenerator.GeneratePepperBase64();

        (await Process().ProvisionPasswordPepperAsync(minted, CancellationToken.None)).Should().BeTrue();

        var configuration = LayeredFromTheFile();
        configuration["Password:Pepper:Keys:1"].Should().Be(minted);
        configuration["Password:Pepper:CurrentKeyId"].Should().Be("1");
    }

    // What Program.cs re-layers after a top-up: configuration from the file as it is now.
    private IConfiguration LayeredFromTheFile() =>
        Auth.Infrastructure.Configuration.SecretConfigurationExtensions
            .AddDpapiSecrets(new ConfigurationBuilder(), _protection, FilePath)
            .Build();
}
