using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Shell;
using Microsoft.Extensions.Logging;

namespace Iris.Features.Auth;

public enum AuthField
{
    Email,
    Password,
}

public sealed partial class AuthViewModel : ObservableObject
{
    private static readonly ProjectionBackground ShowcaseBackground = new("showcase", "Vitral", ["#2A1658", "#0B0814", "#07070B"], false);

    private readonly IAuthService _auth;
    private readonly SessionStore _session;
    private CancellationTokenSource? _showcaseLoop;
    private readonly ILogger<AuthViewModel> _log;

    public AuthViewModel(IAuthService auth, IShowcaseContentProvider showcase, SessionStore session, ILogger<AuthViewModel> log)
    {
        _log = log;
        _auth = auth;
        _session = session;
        ShowcaseItems = showcase.Items();
        Email = session.LastEmail;
        if (session.TakeNotice() is { } notice)
        {
            if (notice.IsSuccess)
            {
                SuccessMessage = notice.Message;
            }
            else
            {
                BannerMessage = notice.Message;
            }
        }
    }

    /// <summary>Asks the view to focus a field (first invalid field after a failed submit).</summary>
    public event EventHandler<AuthField>? FocusRequested;

    public string Title => "Te damos la bienvenida";

    public string Subtitle => "Ingresa con el correo de tu iglesia para preparar el servicio.";

    public string SubmitText => "Entrar";

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    public partial string? BannerMessage { get; set; }

    [ObservableProperty]
    public partial string? SuccessMessage { get; set; }

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

        if (!ValidateSignIn())
        {
            return;
        }

        SuccessMessage = null;

        IsSubmitting = true;
        try
        {
            var session = await _auth.SignInAsync(new SignInCredentials(Email, Password));
            StopShowcase();
            _session.SignIn(session);
        }
        catch (AuthException ex)
        {
            ShowError(ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "El acceso falló de forma inesperada");
            BannerMessage = AuthValidator.Message(AuthError.Unknown);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private void PresentPasswordRecovery() => Recovery = new PasswordRecoveryViewModel(_auth, Email, OnRecoveryCompleted);

    [RelayCommand]
    private void CloseRecovery() => Recovery = null;

    private bool ValidateSignIn()
    {
        EmailError = AuthValidator.Email(Email);
        PasswordError = string.IsNullOrEmpty(Password) ? "Ingresa tu contraseña." : null;
        if (EmailError is not null || PasswordError is not null)
        {
            FocusRequested?.Invoke(this, EmailError is not null ? AuthField.Email : AuthField.Password);
            return false;
        }

        return true;
    }

    // Field errors from the API go under their field; anything else is shown as is in the banner.
    private void ShowError(AuthException ex)
    {
        var placed = false;
        foreach (var (field, message) in ex.FieldErrors)
        {
            switch (field)
            {
                case "email":
                    EmailError = message;
                    break;
                case "password":
                    PasswordError = message;
                    break;
                default:
                    continue;
            }

            placed = true;
        }

        if (!placed)
        {
            BannerMessage = ex.HasServerMessage ? ex.Message : AuthValidator.Message(ex.Error);
        }
    }

    private void OnRecoveryCompleted(string email)
    {
        Recovery = null;
        Email = email;
        Password = string.Empty;
        SuccessMessage = "Tu contraseña quedó actualizada. Inicia sesión con la nueva.";
    }

    private void ClearErrors()
    {
        EmailError = PasswordError = BannerMessage = null;
    }
}
