using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Services;

namespace Iris.Features.Auth;

/// <summary>The three steps of password recovery (IRIS_SPEC §6.1): email → 6-digit code → new password.</summary>
public enum RecoveryStep
{
    Email = 1,
    Code = 2,
    Password = 3,
}

public sealed partial class PasswordRecoveryViewModel : ObservableObject
{
    /// <summary>Seconds before "Reenviar código" becomes available.</summary>
    public const int ResendDelaySeconds = 60;

    private readonly IAuthService _auth;
    private readonly Action<string> _completed;
    private CancellationTokenSource? _countdown;

    public PasswordRecoveryViewModel(IAuthService auth, string email, Action<string> completed)
    {
        _auth = auth;
        _completed = completed;
        Email = email;
    }

    /// <summary>Asks the view to focus the field of the step that just appeared.</summary>
    public event EventHandler? FocusRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailStep), nameof(IsCodeStep), nameof(IsPasswordStep), nameof(StepNumber), nameof(Title), nameof(Description), nameof(PrimaryText))]
    public partial RecoveryStep Step { get; set; } = RecoveryStep.Email;

    public bool IsEmailStep => Step == RecoveryStep.Email;

    public bool IsCodeStep => Step == RecoveryStep.Code;

    public bool IsPasswordStep => Step == RecoveryStep.Password;

    public int StepNumber => (int)Step;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial string Email { get; set; }

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? CodeError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    public partial string? ConfirmError { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResend), nameof(ResendText))]
    public partial int ResendSeconds { get; set; }

    public bool CanResend => ResendSeconds <= 0;

    public string ResendText => $"¿No te llegó? Puedes pedir otro en {ResendSeconds} s.";

    public string Title => Step switch
    {
        RecoveryStep.Email => "Recupera tu acceso",
        RecoveryStep.Code => "Revisa tu correo",
        _ => "Crea tu nueva contraseña",
    };

    public string Description => Step switch
    {
        RecoveryStep.Email => "Escribe el correo de tu iglesia y te enviaremos un código de 6 dígitos.",
        RecoveryStep.Code => $"Si {Email.Trim()} tiene una cuenta de Iris, te llegó un código. Vence en 15 minutos.",
        _ => "Al guardarla cerramos la sesión en todos tus dispositivos y vuelves a iniciar sesión.",
    };

    public string PrimaryText => Step switch
    {
        RecoveryStep.Email => "Enviar código",
        RecoveryStep.Code => "Continuar",
        _ => "Guardar y volver a iniciar sesión",
    };

    partial void OnEmailChanged(string value) => EmailError = Error = null;

    // Digits only, six at most.
    partial void OnCodeChanged(string value)
    {
        var clean = new string(value.Where(char.IsDigit).Take(6).ToArray());
        if (clean != value)
        {
            Code = clean;
            return;
        }

        CodeError = Error = null;
    }

    partial void OnNewPasswordChanged(string value) => PasswordError = Error = null;

    partial void OnConfirmPasswordChanged(string value) => ConfirmError = Error = null;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PrimaryAsync()
    {
        if (IsBusy)
        {
            return;
        }

        switch (Step)
        {
            case RecoveryStep.Email:
                await SendCodeAsync(advance: true);
                break;
            case RecoveryStep.Code:
                await VerifyAsync();
                break;
            default:
                await SaveAsync();
                break;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ResendAsync()
    {
        if (!IsBusy && CanResend)
        {
            await SendCodeAsync(advance: false);
        }
    }

    public void Stop()
    {
        _countdown?.Cancel();
        _countdown = null;
    }

    private async Task SendCodeAsync(bool advance)
    {
        if (advance)
        {
            EmailError = AuthValidator.Email(Email);
            if (EmailError is not null)
            {
                return;
            }
        }

        IsBusy = true;
        try
        {
            await _auth.RequestPasswordResetAsync(Email.Trim());
            if (advance)
            {
                Step = RecoveryStep.Code;
                FocusRequested?.Invoke(this, EventArgs.Empty);
            }

            _ = RunCountdownAsync();
        }
        catch (AuthException ex)
        {
            Error = ex.HasServerMessage ? ex.Message : AuthValidator.Message(ex.Error);
        }
        catch (Exception)
        {
            Error = AuthValidator.Message(AuthError.Unknown);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task VerifyAsync()
    {
        if (Code.Length != 6)
        {
            CodeError = "Escribe los 6 dígitos del código.";
            return;
        }

        IsBusy = true;
        try
        {
            await _auth.VerifyResetCodeAsync(Email.Trim(), Code);
            Step = RecoveryStep.Password;
            FocusRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (AuthException ex)
        {
            if (ex.FieldErrors.TryGetValue("code", out var message))
            {
                CodeError = message;
            }
            else if (ex.Error == AuthError.Network || ex.Code is null)
            {
                Error = ex.HasServerMessage ? ex.Message : AuthValidator.Message(ex.Error);
            }
            else
            {
                CodeError = ex.Message;
            }
        }
        catch (Exception)
        {
            Error = AuthValidator.Message(AuthError.Unknown);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        PasswordError = NewPassword.Length < AuthValidator.MinimumPasswordLength ? "Mínimo 8 caracteres." : null;
        ConfirmError = NewPassword != ConfirmPassword ? "Las contraseñas no coinciden." : null;
        if (PasswordError is not null || ConfirmError is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _auth.ResetPasswordAsync(Email.Trim(), Code, NewPassword, ConfirmPassword);
            Stop();
            _completed(Email.Trim());
        }
        catch (AuthException ex)
        {
            if (ex.Code is "RESET_CODE_INVALID" or "RESET_LIMIT_REACHED")
            {
                // The code stopped being valid: back to the code step with the server's reason.
                Step = RecoveryStep.Code;
                Code = string.Empty;
                CodeError = ex.Message;
                FocusRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (ex.FieldErrors.TryGetValue("password", out var password))
            {
                PasswordError = password;
            }
            else if (ex.FieldErrors.TryGetValue("passwordConfirmation", out var confirm))
            {
                ConfirmError = confirm;
            }
            else
            {
                Error = ex.HasServerMessage ? ex.Message : AuthValidator.Message(ex.Error);
            }
        }
        catch (Exception)
        {
            Error = AuthValidator.Message(AuthError.Unknown);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCountdownAsync()
    {
        Stop();
        var countdown = _countdown = new CancellationTokenSource();
        ResendSeconds = ResendDelaySeconds;
        try
        {
            while (ResendSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), countdown.Token);
                ResendSeconds--;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
