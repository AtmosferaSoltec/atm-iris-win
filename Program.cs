#if DISABLE_XAML_GENERATED_MAIN
using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Iris;

/// <summary>
/// Own entry point (the XAML-generated one is disabled in <c>Iris.csproj</c>) because Velopack must run first:
/// the installer calls the app with special arguments to install, update and uninstall, and it exits right there.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
#endif
