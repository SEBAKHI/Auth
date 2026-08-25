using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The two files that hold the only copy of the signing key are replaced, never
/// overwritten in place.
///
/// Between <c>File.WriteAllBytes</c> truncating a file and writing its last byte,
/// that file is neither the old contents nor the new ones. For this particular
/// file that window contains the JWT signing key, the refresh-token HMAC key, the
/// gateway token, the connection string and the Argon2id pepper, with no second
/// copy of any of them: a recycle, a full disk or a power loss inside it leaves a
/// file that decrypts to nothing and a process that cannot start.
///
/// <c>File.Copy(overwrite: true)</c> has the same window, which is why the plain
/// text writer is checked here too — it used a temp file and then copied out of
/// it, under a comment claiming the result was atomic.
///
/// Asserted against the source because the failure is a crash at a precise
/// instant, and a test that tried to reproduce it would be racing the thing it
/// was measuring.
/// </summary>
public class AtomicSecretWriteTests
{
    public static readonly TheoryData<string, string> SecretWriters = new()
    {
        { "Auth.Infrastructure", "Security/DpapiSecretService.cs" },
        { "Auth.Shared", "Configuration/PlainTextSecretInitializer.cs" },
    };

    [Theory]
    [MemberData(nameof(SecretWriters))]
    public void SecretWriters_PublishByRenaming(string project, string relativePath)
    {
        var source = Read(project, relativePath);

        Regex.IsMatch(source, @"File\.Replace\(").Should().BeTrue(
            $"{relativePath} must publish the new contents with a rename, which within one volume "
            + "either happened or did not");
    }

    [Theory]
    [MemberData(nameof(SecretWriters))]
    public void SecretWriters_DoNotWriteOverTheLiveFile(string project, string relativePath)
    {
        var source = Read(project, relativePath);

        // The target path is the live file. These three write to whatever path
        // they are given, so seeing them here means something is being written in
        // place — the temp file, if there is one, is not doing its job.
        foreach (var inPlace in new[] { "File.WriteAllBytesAsync(_settings.SecretFilePath", "File.WriteAllText(path", "File.Copy(" })
        {
            source.Should().NotContain(inPlace,
                $"{relativePath} would expose a window in which the file is neither the old secrets nor the new ones");
        }
    }

    [Theory]
    [MemberData(nameof(SecretWriters))]
    public void SecretWriters_FlushToDiskBeforePublishing(string project, string relativePath)
    {
        var source = Read(project, relativePath);

        // A rename can be durable while the bytes it publishes are still in the
        // OS cache, which turns an atomic swap into an atomic swap to nothing.
        source.Should().Contain("Flush(flushToDisk: true)",
            $"{relativePath} must reach the device before it renames");
    }

    [Fact]
    public void TemporaryNames_AreUniquePerWrite()
    {
        // A fixed ".tmp" suffix means two processes starting together write the
        // same path, and one publishes a file the other is still writing.
        foreach (var (project, relativePath) in new[]
                 {
                     ("Auth.Infrastructure", "Security/DpapiSecretService.cs"),
                     ("Auth.Shared", "Configuration/PlainTextSecretInitializer.cs"),
                 })
        {
            var source = Read(project, relativePath);

            Regex.IsMatch(source, @"Guid\.NewGuid\(\):N\}\.tmp").Should().BeTrue(
                $"{relativePath} must name its temp file uniquely");
        }
    }

    /// <summary>
    /// The file's code, with whole-line comments removed.
    /// </summary>
    /// <remarks>
    /// Stripped because the banned calls are named in the comments that explain
    /// why they are banned, and a test that cannot tell an explanation from a
    /// call would forbid writing the explanation.
    /// </remarks>
    private static string Read(string project, string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(
            SolutionDirectory(), project, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        return string.Join(
            Environment.NewLine,
            lines.Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                                && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
