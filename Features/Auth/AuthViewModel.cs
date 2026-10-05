using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Shell;

namespace Iris.Features.Auth;

public enum AuthField
{
    ChurchName,
    LeaderName,
    Email,
    Password,
}

public sealed partial class AuthViewModel : ObservableObject
{
    private static readonly ProjectionBackground ShowcaseBackground = new("showcase", "Vitral", ["#2A1658", "#0B0814", "#07070B"], false);

    private readonly IAuthService _auth;
    private readonly SessionStore _session;
    private CancellationTokenSource? _showcaseLoop;

    public AuthViewModel(IAuthService auth, IShowcaseContentProvider showcase, SessionStore session)
    {
        _auth = auth;
        _session = session;
        ShowcaseItems = showcase.Items();
        Email = session.LastEmail;
    }

    /// <summary>Asks the view to focus a field (first invalid field after a failed submit).</summary>
    public event EventHandler<AuthField>? FocusRequested;

    public IList<string> Modes { get; } = ["Iniciar sesión", "Crear cuenta"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignUp), nameof(IsSignIn), nameof(Title), nameof(Subtitle), nameof(SubmitText), nameof(PasswordPlaceholder), nameof(PasswordHint))]
    public partial int ModeIndex { get; set; }

    public bool IsSignUp => ModeIndex == 1;

    public bool IsSignIn => !IsSignUp;

    public string Title => IsSignUp ? "Crea el espacio de tu iglesia" : "Te damos la bienvenida";

    public string Subtitle => IsSignUp
        ? "Configúralo en menos de un minuto y empieza a proyectar."
        : "Ingresa con el correo de tu iglesia para preparar el servicio.";

    public string SubmitText => IsSignUp ? "Crear cuenta" : "Entrar";

    public string PasswordPlaceholder => IsSignUp ? "Crea una contraseña" : "Tu contraseña";

    public string? PasswordHint => IsSignUp ? "Mínimo 8 caracteres." : null;

    [ObservableProperty]
    public partial string ChurchName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LeaderName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ChurchNameError { get; set; }

    [ObservableProperty]
    public partial string? LeaderNameError { get; set; }

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    public partial string? BannerMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsSubmitting { get; set; }

    public bool IsIdle => !IsSubmitting;

    // ----- Rotating mini TV -----

    public IReadOnlyList<ShowcaseItem> ShowcaseItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowcaseFrame))]
    public partial int ShowcaseIndex { get; set; }

    public ProjectionFrame ShowcaseFrame => ShowcaseItems.Count == 0
        ? ProjectionFrame.Black
        : new ProjectionFrame(ShowcaseBackground, new TextContent(ShowcaseItems[ShowcaseIndex].Body, ShowcaseItems[ShowcaseIndex].Footnote));

    // ----- Password recovery -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecoveryOpen))]
    public partial PasswordRecoveryViewModel? Recovery { get; set; }

    public bool IsRecoveryOpen => Recovery is not null;

    partial void OnModeIndexChanged(int value) => ClearErrors();

    partial void OnChurchNameChanged(string value) => ChurchNameError = BannerMessage = null;

    partial void OnLeaderNameChanged(string value) => LeaderNameError = BannerMessage = null;

    partial void OnEmailChanged(string value) => EmailError = BannerMessage = null;

    partial void OnPasswordChanged(string value) => PasswordError = BannerMessage = null;

    /// <summary>Rotates the mini TV every 5 s until <see cref="StopShowcase"/>.</summary>
    public async void RunShowcase()
    {
        StopShowcase();
        var loop = _showcaseLoop = new CancellationTokenSource();
        try
        {
            while (!loop.IsCancellationRequested && ShowcaseItems.Count > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), loop.Token);
                ShowcaseIndex = (ShowcaseIndex + 1) % ShowcaseItems.Count;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void StopShowcase()
    {
        _showcaseLoop?.Cancel();
        _showcaseLoop = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        ClearErrors();

        // Mockup phase: "Entrar" is free (no validation); restore it when the API is connected.
        if (IsSignUp && !ValidateSignUp())
        {
            return;
        }

        IsSubmitting = true;
        try
        {
            var session = IsSignUp
                ? await _auth.SignUpAsync(new SignUpRequest(ChurchName, LeaderName, Email, Password))
                : await _auth.SignInAsync(new SignInCredentials(Email, Password));
            StopShowcase();
            _session.SignIn(session);
        }
        catch (AuthException ex)
        {
            BannerMessage = AuthValidator.Message(ex.Error);
        }
        catch (Exception)
        {
            BannerMessage = AuthValidator.Message(AuthError.Unknown);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private void PresentPasswordRecovery() => Recovery = new PasswordRecoveryViewModel(_auth, Email);

    [RelayCommand]
    private void CloseRecovery() => Recovery = null;

    private bool ValidateSignUp()
    {
        ChurchNameError = AuthValidator.ChurchName(ChurchName);
        LeaderNameError = AuthValidator.LeaderName(LeaderName);
        EmailError = AuthValidator.Email(Email);
        PasswordError = AuthValidator.NewPassword(Password);

        AuthField? firstInvalid =
            ChurchNameError is not null ? AuthField.ChurchName
            : LeaderNameError is not null ? AuthField.LeaderName
            : EmailError is not null ? AuthField.Email
            : PasswordError is not null ? AuthField.Password
            : null;

        if (firstInvalid is { } field)
        {
            FocusRequested?.Invoke(this, field);
            return false;
        }

        return true;
    }

    private void ClearErrors()
    {
        ChurchNameError = LeaderNameError = EmailError = PasswordError = BannerMessage = null;
    }
}
