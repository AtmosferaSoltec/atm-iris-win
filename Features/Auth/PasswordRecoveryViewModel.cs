using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Services;

namespace Iris.Features.Auth;

public enum RecoveryPhase
{
    Editing,
    Sending,
    Sent,
}

public sealed partial class PasswordRecoveryViewModel(IAuthService auth, string email) : ObservableObject
{
    [ObservableProperty]
    public partial string Email { get; set; } = email;

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSending), nameof(IsNotSending), nameof(IsSent), nameof(IsEditing), nameof(Title), nameof(Message))]
    public partial RecoveryPhase Phase { get; set; }

    public bool IsSending => Phase == RecoveryPhase.Sending;

    public bool IsNotSending => !IsSending;

    public bool IsSent => Phase == RecoveryPhase.Sent;

    public bool IsEditing => Phase != RecoveryPhase.Sent;

    public string Title => IsSent ? "Revisa tu correo" : "Recupera tu acceso";

    public string Message => IsSent
        ? "Enviamos un enlace a "
        : "Escribe el correo de tu iglesia y te enviaremos un enlace para crear una nueva contraseña.";

    public string SentEmail => IsSent ? Email.Trim() : string.Empty;

    public string SentSuffix => IsSent ? ". Puede tardar un par de minutos en llegar." : string.Empty;

    partial void OnEmailChanged(string value)
    {
        EmailError = null;
        Error = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SendAsync()
    {
        if (IsSending)
        {
            return;
        }

        EmailError = AuthValidator.Email(Email);
        if (EmailError is not null)
        {
            return;
        }

        Phase = RecoveryPhase.Sending;
        try
        {
            await auth.RequestPasswordResetAsync(Email.Trim());
            Phase = RecoveryPhase.Sent;
            OnPropertyChanged(nameof(SentEmail));
            OnPropertyChanged(nameof(SentSuffix));
        }
        catch (Exception)
        {
            Error = "No pudimos enviar el enlace. Inténtalo de nuevo.";
            Phase = RecoveryPhase.Editing;
        }
    }
}
