namespace Auth_Setup;

/// <summary>
/// What the operator asked Auth_Setup for.
/// </summary>
public enum SetupMode
{
    /// <summary>Set the seeded administrator's address and password (<c>--email</c>).</summary>
    BootstrapAdministrator,

    /// <summary>Print the emergency script that removes an account's second factor (<c>--reset-two-factor</c>).</summary>
    ResetTwoFactor,
}

/// <summary>
/// The parsed command line: a mode and its inputs, or the reason it was refused.
/// </summary>
/// <param name="Mode">The mode, when the command line was accepted.</param>
/// <param name="Address">The address the mode works on, as typed.</param>
/// <param name="Password">The password given on the command line; null means "prompt".</param>
/// <param name="Error">Why the command line was refused; null when it was accepted.</param>
public sealed record SetupRequest(SetupMode? Mode, string? Address, string? Password, string? Error)
{
    /// <summary>Gets whether the command line was refused.</summary>
    public bool IsRefused => Error is not null;

    // The password stays out of any output the record reaches.
    public override string ToString() => $"SetupRequest {{ Mode = {Mode}, Address = {Address}, Error = {Error} }}";
}

/// <summary>
/// The one parser of Auth_Setup's command line. Two modes, and nothing else is
/// accepted: a command line that could mean something other than what it says is
/// refused rather than guessed at.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--email &lt;address&gt; [&lt;password&gt;]</c>: the password positional, or
/// prompted when absent.</item>
/// <item><c>--reset-two-factor &lt;address&gt;</c>.</item>
/// </list>
/// Refused: no mode; both modes; an unknown option; an option given twice; an
/// option without its value; the old form with two positional arguments
/// (<c>"&lt;password&gt;" "&lt;email&gt;"</c>), whose second argument used to SELECT the
/// row and would now SET the address; anything starting with <c>--</c> where a
/// value belongs — so a mistyped option is never taken for a password and printed
/// into an UPDATE; and an address with any character outside printable ASCII. The
/// printed SQL doubles the ASCII quote only, and a console or editor that maps a
/// look-alike (a modifier letter apostrophe, a fullwidth quote) to <c>'</c> on its
/// way to the database would end the literal there.
/// </remarks>
public static class SetupCommandLine
{
    public const string EmailOption = "--email";
    public const string ResetTwoFactorOption = "--reset-two-factor";

    public const string Usage =
        "Usage:\n" +
        "  dotnet run --project Auth/Auth_Setup -- --email <your address> [\"<password>\"]\n" +
        "      Prints the statement that gives the seeded administrator your address and a password.\n" +
        "      Without a password on the command line, it asks for one.\n" +
        "  dotnet run --project Auth/Auth_Setup -- --reset-two-factor <address>\n" +
        "      Prints the emergency statement that removes an account's two-step verification.";

    /// <summary>Parses the command line.</summary>
    public static SetupRequest Parse(IReadOnlyList<string> args)
    {
        string? email = null;
        string? reset = null;
        var positional = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            if (arg != EmailOption && arg != ResetTwoFactorOption)
            {
                return Refuse($"Unknown option {arg}.");
            }

            if ((arg == EmailOption && email is not null) || (arg == ResetTwoFactorOption && reset is not null))
            {
                return Refuse($"{arg} was given twice.");
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return Refuse($"{arg} needs an address after it.");
            }

            i++;
            if (arg == EmailOption)
            {
                email = args[i];
            }
            else
            {
                reset = args[i];
            }
        }

        if (email is not null && reset is not null)
        {
            return Refuse($"{EmailOption} and {ResetTwoFactorOption} cannot be used together. Run the tool once for each.");
        }

        var address = email ?? reset;
        if (address is not null && !IsPrintableAscii(address))
        {
            return Refuse("The address may hold printable ASCII characters only (no accented letters, look-alike quotes or spaces).");
        }

        if (reset is not null)
        {
            return positional.Count == 0
                ? new SetupRequest(SetupMode.ResetTwoFactor, reset, null, null)
                : Refuse($"{ResetTwoFactorOption} takes only an address.");
        }

        if (positional.Count >= 2)
        {
            // The old form: "<password>" "<email>". Its second argument used to pick
            // the row to change; now the address is what is set, so a guess would
            // write a wrong address onto the administrator. Refused, never reread.
            return Refuse(
                "The two-argument form \"<password>\" \"<email>\" is no longer accepted: the address now becomes the " +
                $"administrator's, so it is given by name. Use {EmailOption} <your address> and type the password when asked.");
        }

        if (email is null)
        {
            return Refuse(positional.Count == 1
                ? $"A password alone is no longer enough: also give {EmailOption} <your address>."
                : "Nothing to do: choose a mode.");
        }

        return new SetupRequest(SetupMode.BootstrapAdministrator, email, positional.FirstOrDefault(), null);
    }

    /// <summary>
    /// Whether a password may be used: never one that starts with <c>--</c>, which is
    /// how a mistyped option would arrive in the password's place — from the command
    /// line or pasted at the prompt.
    /// </summary>
    public static bool IsAcceptablePassword(string password) =>
        !password.StartsWith("--", StringComparison.Ordinal);

    /// <summary>
    /// Printable ASCII, the space excluded: every character an address the SQL
    /// literal can carry unchanged through any console, file or tool.
    /// </summary>
    private static bool IsPrintableAscii(string value) =>
        value.All(character => character is > ' ' and <= '~');

    private static SetupRequest Refuse(string error) => new(null, null, null, error);
}
