using System.Text.RegularExpressions;
using Iris.Core.Services;

namespace Iris.Features.Auth;

public static partial class AuthValidator
{
    public const int MinimumPasswordLength = 8;

    public static string? ChurchName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Escribe el nombre de tu iglesia." : null;

    public static string? LeaderName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Escribe el nombre del responsable." : null;

    public static string? Email(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Ingresa el correo de tu iglesia."
        : !EmailPattern().IsMatch(value.Trim()) ? "Ese correo no parece válido."
        : null;

    public static string? NewPassword(string value) =>
        string.IsNullOrEmpty(value) ? "Ingresa tu contraseña."
        : value.Length < MinimumPasswordLength ? "Usa al menos 8 caracteres."
        : null;

    public static string Message(AuthError error) => error switch
    {
        AuthError.InvalidCredentials => "El correo o la contraseña no coinciden. Revísalos e inténtalo de nuevo.",
        AuthError.EmailAlreadyInUse => "Ya existe una cuenta con ese correo. Intenta iniciar sesión.",
        AuthError.Network => "No pudimos conectarnos. Verifica tu conexión a internet.",
        _ => "Algo salió mal. Inténtalo de nuevo.",
    };

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
