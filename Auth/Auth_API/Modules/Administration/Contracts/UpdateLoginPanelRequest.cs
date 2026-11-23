namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to show or hide the panel beside the sign-in form.
/// </summary>
/// <param name="Shown">
/// True: the sign-in pages have the panel, filled by the active design or plain. False: the
/// platform's sign-in pages have no panel, nor has an application's page without an active
/// design of its own.
/// </param>
public record UpdateLoginPanelRequest(bool Shown);
