using System;
using Iris.Core.Networking;
using Iris.Shell;
using Iris.Shell.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Iris;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Anything that escapes the app ends up in the log file before the process goes down.
        UnhandledException += (_, e) =>
            Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("App").LogCritical(e.Exception, "Error no controlado: {Message}", e.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("App").LogCritical(e.ExceptionObject as Exception, "Error no controlado en un hilo de fondo");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("App").LogError(e.Exception, "Tarea sin observar con error");
    }

    public static IAppSettings Settings { get; } = new LocalAppSettings();

    public static IServiceProvider Services { get; } = AppDependencies.Create(Settings);

    public static MainWindow MainWindow { get; private set; } = null!;

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
