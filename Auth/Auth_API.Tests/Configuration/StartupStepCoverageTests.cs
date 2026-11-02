using System.Text;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// X03: every startup step that can stop the boot runs inside <c>StartupStep.Run</c>, which logs
/// the reason at Fatal and flushes before rethrowing. Outside it, a refusal dies before Serilog
/// writes it, and under IIS the operator sees a bare 500.30 with an empty log. Nothing in a unit
/// test resolves Program.cs, so this reads its source.
/// </summary>
public class StartupStepCoverageTests
{
    /// <summary>Each step by name, and the calls that must sit inside it.</summary>
    public static TheoryData<string, string> StepsAndTheirCalls => new()
    {
        { "storage mode resolution", "AuthDataProtectionExtensions.ParseStorageMode(" },
        { "storage mode resolution", "AuthDataProtectionExtensions.ResolveStorageMode(" },
        { "production plaintext-secret check", "ProductionSecretGuard.EnsureNoPlaintextSecrets(" },
        { "data protection key ring setup", "ConfigureSecretKeyRing(builder.Services.AddDataProtection())" },
        { "secrets file load", "() => Auth.Infrastructure.Configuration.SecretConfigurationExtensions.AddDpapiSecrets(" },
        { "plain-text secret generation", "PlainTextSecretInitializer.EnsureSecrets(" },
        { "first-start key generation", ".GenerateMissingKeysAsync(" },
        { "identifier-key top-up", "existingSecrets.AccountDeletionIdentifierHmacKey = mintedIdentifierKey" },
        { "required-secrets check", "RequiredSecretsGuard.EnsureAllPresent(" },
        { "key material validation", "HmacKeyringConfiguration.Load(" },
        { "key material validation", "KeyMaterialValidator.Validate(" },
        { "connection-string resolution check", "connectionString = builder.Configuration.GetConnectionString(" },
        { "connection-string resolution check", "ConnectionStringGuard.EnsureResolved(" },
        { "password pepper provisioning", "KeyMaterialGenerator.GeneratePepperBase64()" },
        { "signing key load", "new JwtTokenService(" },
    };

    [Theory]
    [MemberData(nameof(StepsAndTheirCalls))]
    public void Program_EveryCallThatCanStopTheBoot_RunsInsideItsNamedStep(string step, string call)
    {
        var program = ProgramSource.Read();

        var occurrences = program.Occurrences(call);
        occurrences.Should().NotBeEmpty($"Program.cs makes the call {call}");
        foreach (var index in occurrences)
        {
            program.StepAround(index).Should().Be(step, $"{call} at offset {index} must run inside \"{step}\"");
        }
    }

    [Theory]
    [InlineData("plain-text secret generation", "beforeGenerate:", "beforeGenerate: secrets => EnsureSafeToGenerate(secrets, builder.Configuration.GetConnectionString(\"AuthDb\"))")]
    [InlineData("first-start key generation", "beforeSave:", "beforeSave: generated => EnsureSafeToGenerate(KeyRegenerationGuard.GeneratedBy(generated), builder.Configuration.GetConnectionString(\"AuthDb\"))")]
    [InlineData("identifier-key top-up", "EnsureSafeToGenerate(", "EnsureSafeToGenerate([GeneratedSecret.IdentifierKey], builder.Configuration.GetConnectionString(\"AuthDb\"))")]
    [InlineData("password pepper provisioning", "EnsureSafeToGenerate(", "EnsureSafeToGenerate([GeneratedSecret.PasswordPepper], connectionString)")]
    public void Program_EveryGenerationPath_ChecksTheDatabase_WithWhatItGeneratesAndTheConnectionString(
        string step, string start, string expected)
    {
        // The arguments are pinned, not just the call: passing no secrets, the wrong key or no
        // connection string would turn the check into a no-op with the call still in place.
        var program = ProgramSource.Read();

        var calls = program.Occurrences(start).Where(index => program.StepAround(index) == step).ToList();

        calls.Should().ContainSingle($"\"{step}\" asks KeyRegenerationGuard once before it generates");
        ProgramSource.Squeezed(program.CallText(calls[0], "EnsureSafeToGenerate("))
            .Should().Be(ProgramSource.Squeezed(expected));
    }

    [Fact]
    public void Program_TheCheck_PassesEverythingToTheGuard_AndAddsAPepperAboutToBeMinted()
    {
        var program = ProgramSource.Read();

        var guardCall = program.Occurrences("KeyRegenerationGuard.EnsureSafeToGenerate(").Should().ContainSingle().Which;
        ProgramSource.Squeezed(program.CallText(guardCall, "KeyRegenerationGuard.EnsureSafeToGenerate(")).Should().Be(
            ProgramSource.Squeezed(
                "KeyRegenerationGuard.EnsureSafeToGenerate(" +
                "PepperIsMissing() ? secrets.Append(GeneratedSecret.PasswordPepper).Distinct().ToList() : secrets, " +
                "connectionStringToCheck, builder.Environment.IsDevelopment(), " +
                "Environment.GetEnvironmentVariable(KeyRegenerationGuard.OverrideVariable), " +
                "DateOnly.FromDateTime(DateTime.UtcNow))"),
            "a pepper the later step will mint is checked by the earlier paths too, so its refusal never follows their write");
        program.Occurrences("if (PepperIsMissing())").Should().ContainSingle(
            "the pepper step and the earlier checks decide \"a pepper will be minted\" with one rule");
    }

    [Fact]
    public void Program_TheTopUp_ReadsTheFileBeforeItChecksAndWrites()
    {
        // A file that does not decrypt also reads as "no identifier key": reading it first
        // stops on the decryption error instead of a check that blames a missing file.
        var program = ProgramSource.Read();
        const string step = "identifier-key top-up";

        var read = program.Occurrences("topUpService.LoadSecretsAsync(").Single(index => program.StepAround(index) == step);
        var check = program.Occurrences("EnsureSafeToGenerate(").Single(index => program.StepAround(index) == step);

        read.Should().BeLessThan(check);
    }

    [Theory]
    [InlineData("identifier-key top-up", "topUpService.UpdateAsync<")]
    [InlineData("password pepper provisioning", "KeyMaterialGenerator.GeneratePepperBase64()")]
    public void Program_TheChecksThatRunInProgram_ComeBeforeTheWrite(string step, string write)
    {
        // The plain-text and first-start paths check inside the generator (their own tests);
        // these two check in Program.cs, so the order is pinned here.
        var program = ProgramSource.Read();

        var check = program.Occurrences("EnsureSafeToGenerate(").Single(index => program.StepAround(index) == step);
        var written = program.Occurrences(write).Single(index => program.StepAround(index) == step);

        check.Should().BeLessThan(written, $"\"{step}\" refuses before anything is written");
    }

    [Fact]
    public void Program_TheCheckAfterTheWrite_IsGone()
    {
        // The post-mint IdentifierKeyRegenerationGuard checked the registry after the new key was
        // already in the file, and on the top-up path only.
        ProgramSource.Read().Occurrences("IdentifierKeyRegenerationGuard").Should().BeEmpty();
    }

    /// <summary>Program.cs with comments and string contents blanked, so only code is searched.</summary>
    private sealed class ProgramSource
    {
        private readonly string _original;
        private readonly string _code;
        private readonly List<(int Start, int End, string Name)> _steps = [];

        private ProgramSource(string original)
        {
            _original = original;
            _code = BlankCommentsAndStrings(original);

            const string marker = "StartupStep.Run(";
            for (var start = _code.IndexOf(marker, StringComparison.Ordinal);
                 start >= 0;
                 start = _code.IndexOf(marker, start + 1, StringComparison.Ordinal))
            {
                var open = start + marker.Length - 1;
                var nameStart = _original.IndexOf('"', open) + 1;
                var name = _original[nameStart.._original.IndexOf('"', nameStart)];
                _steps.Add((open, MatchingParenthesis(open), name));
            }

            _steps.Should().NotBeEmpty("Program.cs runs its boot steps through StartupStep.Run");
        }

        /// <summary>The original text of the call to <paramref name="call"/> found from <paramref name="start"/>, to its closing parenthesis.</summary>
        public string CallText(int start, string call)
        {
            var open = _code.IndexOf(call, start, StringComparison.Ordinal) + call.Length - 1;
            return _original[start..(MatchingParenthesis(open) + 1)];
        }

        /// <summary><paramref name="text"/> without any whitespace, so line breaks never decide a comparison.</summary>
        public static string Squeezed(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        public static ProgramSource Read() =>
            new(File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "Program.cs")));

        public List<int> Occurrences(string token)
        {
            var found = new List<int>();
            for (var index = _code.IndexOf(token, StringComparison.Ordinal);
                 index >= 0;
                 index = _code.IndexOf(token, index + 1, StringComparison.Ordinal))
            {
                found.Add(index);
            }

            return found;
        }

        /// <summary>The innermost step whose parentheses enclose <paramref name="index"/>, or null.</summary>
        public string? StepAround(int index) => _steps
            .Where(step => step.Start < index && index < step.End)
            .OrderByDescending(step => step.Start)
            .Select(step => step.Name)
            .FirstOrDefault();

        private int MatchingParenthesis(int open)
        {
            var depth = 0;
            for (var i = open; i < _code.Length; i++)
            {
                depth += _code[i] switch { '(' => 1, ')' => -1, _ => 0 };
                if (depth == 0)
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Unbalanced StartupStep.Run at offset {open}.");
        }

        private static string BlankCommentsAndStrings(string source)
        {
            var code = new StringBuilder(source);
            var i = 0;
            while (i < source.Length)
            {
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        code[i++] = ' ';
                    }
                }
                else if (source[i] == '"')
                {
                    var verbatim = i > 0 && (source[i - 1] == '@' || (i > 1 && source[i - 2] == '@'));
                    i++;
                    while (i < source.Length)
                    {
                        if (!verbatim && source[i] == '\\')
                        {
                            code[i++] = ' ';
                        }
                        else if (source[i] == '"')
                        {
                            if (verbatim && i + 1 < source.Length && source[i + 1] == '"')
                            {
                                code[i++] = ' ';
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (i < source.Length)
                        {
                            code[i++] = ' ';
                        }
                    }

                    i++;
                }
                else
                {
                    i++;
                }
            }

            return code.ToString();
        }
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
