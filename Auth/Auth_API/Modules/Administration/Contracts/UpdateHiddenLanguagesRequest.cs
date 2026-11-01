namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to set the languages hidden from the language menus of both apps.
/// </summary>
/// <param name="HiddenLanguages">
/// The language codes to hide (<c>ar</c>, <c>tr</c>, <c>fr</c>, <c>zh</c>, <c>ur</c>, <c>fa</c>);
/// an empty list or null shows every language. English (<c>en</c>) cannot be hidden.
/// </param>
public record UpdateHiddenLanguagesRequest(IReadOnlyList<string?>? HiddenLanguages);
