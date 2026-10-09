using Auth_Setup;

namespace Auth_API.Tests.Setup;

/// <summary>
/// Auth_Setup's one parser (S08 PR B, design D-B4; OI-50 B1): two modes, and
/// nothing else accepted — a command line that could mean something other than
/// what it says is refused, never guessed at.
/// </summary>
public class SetupCommandLineTests
{
    [Fact]
    public void Email_WithoutPassword_Prompts()
    {
        var request = SetupCommandLine.Parse(["--email", "operator@example.org"]);

        request.IsRefused.Should().BeFalse();
        request.Mode.Should().Be(SetupMode.BootstrapAdministrator);
        request.Address.Should().Be("operator@example.org");
        request.Password.Should().BeNull("no password on the command line means the tool asks for it");
    }

    [Theory]
    [InlineData("--email", "operator@example.org", "S3cret-pass")]
    [InlineData("S3cret-pass", "--email", "operator@example.org")]
    public void Email_WithOnePositionalPassword_IsAccepted_InEitherOrder(string a, string b, string c)
    {
        var request = SetupCommandLine.Parse([a, b, c]);

        request.IsRefused.Should().BeFalse();
        request.Mode.Should().Be(SetupMode.BootstrapAdministrator);
        request.Address.Should().Be("operator@example.org");
        request.Password.Should().Be("S3cret-pass");
    }

    [Fact]
    public void ResetTwoFactor_IsAccepted()
    {
        var request = SetupCommandLine.Parse(["--reset-two-factor", "lost@example.org"]);

        request.IsRefused.Should().BeFalse();
        request.Mode.Should().Be(SetupMode.ResetTwoFactor);
        request.Address.Should().Be("lost@example.org");
    }

    public static TheoryData<string[], string> Refusals => new()
    {
        // No mode.
        { [], "Nothing to do" },
        // A password alone: the old one-argument form now also needs the address.
        { ["S3cret-pass"], "also give --email" },
        // The old two-positional form: its second argument would now SET the address.
        { ["S3cret-pass", "operator@example.org"], "two-argument form" },
        { ["--email", "operator@example.org", "S3cret-pass", "extra"], "two-argument form" },
        // Both modes.
        { ["--email", "a@example.org", "--reset-two-factor", "b@example.org"], "cannot be used together" },
        // An unknown option.
        { ["--password", "x"], "Unknown option --password" },
        // An option given twice.
        { ["--email", "a@example.org", "--email", "b@example.org"], "--email was given twice" },
        { ["--reset-two-factor", "a@example.org", "--reset-two-factor", "b@example.org"], "--reset-two-factor was given twice" },
        // An option without its value, or with another option in its place.
        { ["--email"], "--email needs an address" },
        { ["--email", "--reset-two-factor", "a@example.org"], "--email needs an address" },
        // A password starting with "--" never reaches the statement: it is an option.
        { ["--email", "operator@example.org", "--S3cret"], "Unknown option --S3cret" },
        // The emergency mode takes nothing but its address.
        { ["--reset-two-factor", "a@example.org", "S3cret-pass"], "takes only an address" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void CommandLine_IsRefused_WithItsReason(string[] args, string reason)
    {
        var request = SetupCommandLine.Parse(args);

        request.IsRefused.Should().BeTrue(string.Join(' ', args));
        request.Mode.Should().BeNull();
        request.Error.Should().Contain(reason);
    }

    [Theory]
    [InlineData("--S3cret", false)]
    [InlineData("--", false)]
    [InlineData("-S3cret", true)]
    [InlineData("S3cret--", true)]
    public void PromptedPassword_StartingWithTwoDashes_IsRefused(string password, bool acceptable)
    {
        SetupCommandLine.IsAcceptablePassword(password).Should().Be(acceptable);
    }

    // F6: the printed SQL doubles the ASCII quote only. A console or editor that
    // maps a look-alike to ' on its way to the database would end the literal
    // there, so an address outside printable ASCII is refused before any SQL exists.
    [Theory]
    [InlineData("--email", "oʼneil@example.org")]
    [InlineData("--email", "o＇neil@example.org")]
    [InlineData("--reset-two-factor", "oʼneil@example.org")]
    [InlineData("--reset-two-factor", "o＇neil@example.org")]
    public void Address_OutsidePrintableAscii_IsRefused_InEitherMode(string option, string address)
    {
        var request = SetupCommandLine.Parse([option, address]);

        request.IsRefused.Should().BeTrue();
        request.Error.Should().Contain("printable ASCII");
        request.Address.Should().BeNull("no SQL is built from a refused address");
    }

    [Fact]
    public void Address_WithTheAsciiApostrophe_IsAccepted()
    {
        // The golden file's address: the ASCII quote is the one the literal doubles.
        SetupCommandLine.Parse(["--reset-two-factor", "o'neil@example.org"]).IsRefused.Should().BeFalse();
    }

    [Fact]
    public void Usage_NamesBothModes()
    {
        SetupCommandLine.Usage.Should().Contain("--email <your address>").And.Contain("--reset-two-factor <address>");
    }
}
