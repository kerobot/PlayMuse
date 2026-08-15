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
        if (serviceProvider is not null)
        {
            ILogger<App>? logger = null;
            try
            {
                // ロガーを取得してログ出力を開始
                logger = serviceProvider.GetService<ILogger<App>>();
                if (logger is not null && logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("App.OnExit: アプリケーション終了処理を開始します。");
                }

                // IAudioPlaybackService を明示的に先行 Dispose
                var playbackService = serviceProvider.GetService<IAudioPlaybackService>();
                if (playbackService is not null)
                {
                    if (logger is not null && logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("App.OnExit: IAudioPlaybackService の先行解放を開始します。");
                    }

                    playbackService.Dispose();

                    if (logger is not null && logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("App.OnExit: IAudioPlaybackService の先行解放が完了しました。");
                    }
                }
            }
            catch (Exception ex)
            {
                // 先行Disposeでの例外はログ出力のみ（ユーザー通知は行わない）
                if (logger is not null)
                {
                    logger.LogWarning(ex, "App.OnExit: IAudioPlaybackService の先行解放時に例外が発生しましたが、処理を続行します。");
                }
            }

            try
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("App.OnExit: 残りのサービスコンテナを解放します。");
                }

                serviceProvider.Dispose();

                if (logger is not null && logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("App.OnExit: 終了処理が完了しました。");
                }
            }
            catch (Exception ex)
            {
                // serviceProvider.Dispose() での例外はログ出力のみ
                if (logger is not null)
                {
                    logger.LogWarning(ex, "App.OnExit: サービスコンテナ解放時に例外が発生しました。");
                }
            }
        }

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
        services.AddSingleton<ISpectrumAnalyzerService, SpectrumAnalyzerService>();
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
