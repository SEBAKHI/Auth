using System.Text.Json;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>
/// What a new design starts from when its author sends no content: a headline and a sentence
/// on the appearance's own surfaces, in the seven languages.
/// </summary>
/// <remarks>
/// It names no colour, size or font of its own. Every value is one of the appearance's
/// variables, so it follows the platform's appearance (or the application's, while that is
/// switched on) in light and dark mode, and changes with it.
/// </remarks>
public static class LoginDesignStarter
{
    /// <summary>The starter's HTML.</summary>
    public const string Content = """
        <style>
          body { margin: 0; }
          .panel {
            box-sizing: border-box;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            justify-content: flex-end;
            gap: 1rem;
            padding: 3rem;
            background:
              radial-gradient(circle at 15% 15%, color-mix(in oklch, var(--primary) 22%, transparent), transparent 55%),
              var(--muted);
          }
          .panel h2 { margin: 0; font-size: 2rem; line-height: 1.25; font-weight: 600; }
          .panel p { margin: 0; max-width: 32rem; line-height: 1.6; color: var(--muted-foreground); }
        </style>
        <div class="panel" dir="{{ dir }}" lang="{{ lang }}">
          <h2>{{ strings.headline | raw }}</h2>
          <p>{{ strings.body | raw }}</p>
        </div>
        """;

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new()
        {
            ["headline"] = "Welcome to {{ App.Name }}",
            ["body"] = "Sign in to continue. Your account is protected by {{ Platform.Name }}."
        },
        ["ar"] = new()
        {
            ["headline"] = "أهلًا بك في {{ App.Name }}",
            ["body"] = "سجّل الدخول للمتابعة. حسابك محميّ بواسطة {{ Platform.Name }}."
        },
        ["tr"] = new()
        {
            ["headline"] = "{{ App.Name }} uygulamasına hoş geldiniz",
            ["body"] = "Devam etmek için oturum açın. Hesabınız {{ Platform.Name }} tarafından korunuyor."
        },
        ["fr"] = new()
        {
            ["headline"] = "Bienvenue sur {{ App.Name }}",
            ["body"] = "Connectez-vous pour continuer. Votre compte est protégé par {{ Platform.Name }}."
        },
        ["zh"] = new()
        {
            ["headline"] = "欢迎使用 {{ App.Name }}",
            ["body"] = "登录以继续。您的账户由 {{ Platform.Name }} 保护。"
        },
        ["ur"] = new()
        {
            ["headline"] = "{{ App.Name }} میں خوش آمدید",
            ["body"] = "جاری رکھنے کے لیے سائن ان کریں۔ آپ کا اکاؤنٹ {{ Platform.Name }} کے ذریعے محفوظ ہے۔"
        },
        ["fa"] = new()
        {
            ["headline"] = "به {{ App.Name }} خوش آمدید",
            ["body"] = "برای ادامه وارد شوید. حساب شما توسط {{ Platform.Name }} محافظت می‌شود."
        }
    };

    /// <summary>The starter's strings document.</summary>
    public static string StringsJson { get; } = JsonSerializer.Serialize(Strings);
}
