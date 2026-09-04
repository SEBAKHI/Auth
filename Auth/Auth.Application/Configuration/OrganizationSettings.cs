namespace Auth.Application.Configuration;

/// <summary>
/// Who may bring a new organization into existence.
/// </summary>
/// <remarks>
/// Deliberately its own section rather than a third field on
/// <see cref="RegistrationSettings"/>, whose subject is who may create an
/// ACCOUNT. Creating an organization creates no account; what it creates is
/// authority — the creator becomes its owner, and the seeded owner role carries
/// the whole <c>org:*</c> family, invitation included. Filing this under
/// registration would make one screen answer two different questions, and the
/// operator who closed public sign-up would reasonably read it as already closed.
/// <para>
/// The reason a switch exists at all: the create endpoint is authenticated and
/// nothing more, so any signed-in user could mint an organization and become its
/// owner. That is a shipped end-user feature rather than an oversight — the
/// accounts app puts the button on its organizations page for everyone — but it
/// is also the amplifier that decided the size of the population able to reach
/// the invitation surface. A deployment that does not want its users creating
/// organizations needs to be able to say so without deleting the feature for the
/// deployments that do.
/// </para>
/// <para>
/// A permission would have been the wrong control. Granting a new code to every
/// user is a no-op dressed as a fix, and not granting it deletes a shipped
/// capability from the product — neither is a security decision, and both cost a
/// seeded row, a catalogue entry, a console mirror and a deploy ordered against
/// the API build.
/// </para>
/// </remarks>
public class OrganizationSettings
{
    public const string SectionName = "Organizations";

    /// <summary>
    /// Gets or sets whether an ordinary signed-in user may create an
    /// organization. Open by default, which is what every deployment had before
    /// this switch existed.
    /// <para>
    /// Closing it does not disturb organizations that already exist, and does not
    /// stop a platform administrator: a caller holding
    /// <c>organizations:manage</c> passes regardless, because the switch governs
    /// self-service rather than administration.
    /// </para>
    /// </summary>
    public bool AllowSelfServiceCreation { get; set; } = true;
}
