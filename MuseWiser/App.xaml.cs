using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MuseWiser.Models;
using MuseWiser.Models.Enums;
using MuseWiser.Models.Weather;
using MuseWiser.Services.Audio;
using MuseWiser.Services.Audio.PlayMuse;
using MuseWiser.Services.Interop;
using MuseWiser.Services.Settings;
using MuseWiser.Services.Weather;
using MuseWiser.ViewModels;

namespace MuseWiser
{
    /// <summary>
    /// アプリケーションのエントリポイント。DI ホストの構築・起動、タスクトレイアイコン、メインウィンドウ表示を行う。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// DI コンテナとホストライフサイクルを管理するホスト。
        /// </summary>
        private IHost? _host;

        /// <summary>
        /// タスクトレイアイコン。
        /// </summary>
        private TaskbarIcon? _trayIcon;

        /// <summary>
        /// アプリケーション全体用のロガー。
        /// </summary>
        private ILogger<App>? _logger;

        /// <summary>
        /// アプリケーション起動時に DI ホストを構築し、タスクトレイアイコンとメインウィンドウを表示する。
        /// </summary>
        /// <param name="e">起動イベント引数。</param>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                _host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                        config.AddEnvironmentVariables();
                    })
                    .ConfigureServices(ConfigureServices)
                    .Build();

                await _host.StartAsync();

                _logger = _host.Services.GetRequiredService<ILogger<App>>();

                _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var iconStream = assembly.GetManifestResourceStream("MuseWiser.Resources.Icons.MuseWiser.ico"))
                {
                    if (iconStream is not null)
                    {
                        _trayIcon.Icon = new System.Drawing.Icon(iconStream);
                    }
                }

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();

                var settingsService = _host.Services.GetRequiredService<ISettingsService>();
                var displaySettings = settingsService.Load();
                mainWindow.SourceInitialized += (_, _) =>
                {
                    WindowPositionHelper.Apply(mainWindow, displaySettings, ((MainViewModel)mainWindow.DataContext).Weather.HasData);
                    WindowZOrderHelper.Apply(mainWindow, displaySettings);
                };

                mainWindow.Show();
            }
            catch (Exception ex)
            {
                _logger?.LogCritical(ex, "アプリケーションの起動に失敗しました。");
                MessageBox.Show($"アプリケーションの起動に失敗しました。\n{ex.Message}", "MuseWiser", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        /// <summary>
        /// DI コンテナに各サービス・ViewModel・ウィンドウを登録する。
        /// </summary>
        /// <param name="context">ホスト構築コンテキスト（設定ファイル含む）。</param>
        /// <param name="services">登録先のサービスコレクション。</param>
        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<ClockViewModel>();

            services.Configure<WeatherSettings>(context.Configuration.GetSection("Weather"));
            services.AddHttpClient<IWeatherService, OpenWeatherService>();
            services.AddSingleton<WeatherViewModel>();

            services.Configure<PlayMusePipeOptions>(context.Configuration.GetSection("AudioSource"));
            services.AddSingleton<WasapiLoopbackAudioService>();
            services.AddSingleton<PlayMuseNamedPipeAudioCaptureService>();
            services.AddSingleton<AdaptiveAudioCaptureService>();
            services.AddSingleton<IAudioCaptureService>(sp =>
            {
                var mode = sp.GetRequiredService<IOptions<PlayMusePipeOptions>>().Value.Mode;
                return mode switch
                {
                    AudioSourceMode.PlayMusePipe => sp.GetRequiredService<PlayMuseNamedPipeAudioCaptureService>(),
                    AudioSourceMode.Auto => sp.GetRequiredService<AdaptiveAudioCaptureService>(),
                    _ => sp.GetRequiredService<WasapiLoopbackAudioService>(),
                };
            });
            services.AddSingleton<VolumeMeterViewModel>();
            services.AddSingleton<AudioDeviceInfoViewModel>();

            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsWindow>();
        }

        /// <summary>
        /// タスクトレイメニューから設定画面を表示し、保存時にメインウィンドウへ設定を再適用する。
        /// </summary>
        /// <param name="sender">イベント送信元。</param>
        /// <param name="e">ルーティングイベント引数。</param>
        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_host is null)
            {
                return;
            }

            try
            {
                var settingsWindow = _host.Services.GetRequiredService<SettingsWindow>();
                var viewModel = (SettingsViewModel)settingsWindow.DataContext;
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();

                viewModel.SettingsSaved += (_, settings) =>
                {
                    var mainViewModel = (MainViewModel)mainWindow.DataContext;
                    WindowPositionHelper.Apply(mainWindow, settings, mainViewModel.Weather.HasData);
                    WindowZOrderHelper.Apply(mainWindow, settings);
                    mainViewModel.Weather.RefreshNow();
                };

                settingsWindow.Owner = mainWindow;
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "設定画面の表示に失敗しました。");
            }
        }

        /// <summary>
        /// タスクトレイメニューからアプリケーションを終了する。
        /// </summary>
        /// <param name="sender">イベント送信元。</param>
        /// <param name="e">ルーティングイベント引数。</param>
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Shutdown();
        }

        /// <summary>
        /// アプリケーション終了時にタスクトレイアイコン、ViewModel、DI ホストを解放し、未処理例外ハンドラの購読を解除する。
        /// </summary>
        /// <param name="e">終了イベント引数。</param>
        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                _trayIcon?.Dispose();

                if (_host is not null)
                {
                    var mainViewModel = _host.Services.GetService<MainViewModel>();
                    if (mainViewModel is not null)
                    {
                        await mainViewModel.DisposeAsync();
                    }

                    await _host.StopAsync();
                    _host.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "アプリケーション終了処理中にエラーが発生しました。");
            }
            finally
            {
                DispatcherUnhandledException -= OnDispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            }

            base.OnExit(e);
        }

        /// <summary>
        /// UI スレッドで発生した未処理例外をログ出力し、アプリケーションのクラッシュを防ぐ。
        /// </summary>
        /// <param name="sender">イベント送信元。</param>
        /// <param name="e">未処理例外イベント引数。</param>
        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.LogCritical(e.Exception, "UI スレッドで未処理の例外が発生しました。");
            e.Handled = true;
        }

        /// <summary>
        /// アプリケーションドメイン全体で発生した未処理例外をログ出力する。
        /// </summary>
        /// <param name="sender">イベント送信元。</param>
        /// <param name="e">未処理例外イベント引数。</param>
        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                _logger?.LogCritical(ex, "アプリケーションドメインで未処理の例外が発生しました。");
            }
        }

        /// <summary>
        /// 監視されていないタスク例外をログ出力し、例外を観測済みとしてマークする。
        /// </summary>
        /// <param name="sender">イベント送信元。</param>
        /// <param name="e">未観測タスク例外イベント引数。</param>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _logger?.LogError(e.Exception, "監視されていないタスク例外が発生しました。");
            e.SetObserved();
        }
    }
}
