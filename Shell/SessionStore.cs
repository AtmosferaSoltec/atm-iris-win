using CommunityToolkit.Mvvm.ComponentModel;
using Iris.Core.Models;

namespace Iris.Shell;

/// <summary>Holds the signed-in session. No session → access screen; session → live console.</summary>
public sealed partial class SessionStore : ObservableObject
{
    [ObservableProperty]
    public partial UserSession? Session { get; private set; }

    /// <summary>Kept after signing out so the access screen can prefill it (the password is not kept).</summary>
    public string LastEmail { get; private set; } = string.Empty;

    public void SignIn(UserSession session)
    {
        LastEmail = session.Email;
        Session = session;
    }

    public void SignOut() => Session = null;
}
