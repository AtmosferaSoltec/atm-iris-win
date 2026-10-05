using System;
using Iris.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Iris;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    public static IServiceProvider Services { get; } = AppDependencies.Mock();

    public static MainWindow MainWindow { get; private set; } = null!;

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
