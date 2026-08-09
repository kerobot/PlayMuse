using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayMuse.Core.Services;
using PlayMuse.Core.ViewModels;
using PlayMuse.Services;

namespace PlayMuse;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var services = new ServiceCollection();
        ConfigureServices(services);
        serviceProvider = services.BuildServiceProvider();

        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました。\n\n{e.Exception.Message}",
            "PlayMuse",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<IDispatcherService, WpfDispatcherService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
    }
}
