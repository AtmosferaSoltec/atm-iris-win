namespace Iris.Core;

/// <summary>Places the app sends people to. Accounts, lyrics, music and media are managed on the web.</summary>
public static class WebLinks
{
    public const string App = "https://iris.atmosferast.com";

    /// <summary>
    /// Folder with the installer feed (<c>releases.win.json</c>). It lives on the web's own host, so the existing
    /// wildcard certificate covers it (nginx serves it as static files; docs/plans/12).
    /// </summary>
    public const string WindowsFeed = App + "/downloads/windows/";

    /// <summary>Fixed link the landing page (<c>/downloads</c>) points to; always the latest installer.</summary>
    public const string WindowsInstaller = WindowsFeed + "Iris-Setup.exe";
}
