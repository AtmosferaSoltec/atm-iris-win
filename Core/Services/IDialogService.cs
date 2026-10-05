using System.Threading.Tasks;

namespace Iris.Core.Services;

/// <summary>Modal confirmations asked by view models (the implementation lives in the window layer).</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. Returns true when the person confirms.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false, string cancelText = "Cancelar");

    /// <summary>Shows a message with a single "Entendido" button.</summary>
    Task ShowMessageAsync(string title, string message);
}
