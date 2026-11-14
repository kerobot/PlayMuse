using System.Windows;
using System.Windows.Interop;
using MuseWiser.Services.Interop;
using MuseWiser.Services.Settings;
using MuseWiser.ViewModels;

namespace MuseWiser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// <summary>
    /// メインウィンドウ。音量メーター・時計・天気を表示するクリックスルーオーバーレイウィンドウ。
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 表示設定の読み込み・保存を行うサービス。
        /// </summary>
        private readonly ISettingsService _settingsService;

        /// <summary>
        /// メインウィンドウの ViewModel。
        /// </summary>
        private readonly MainViewModel _viewModel;

        /// <summary>
        /// <see cref="MainWindow"/> の新しいインスタンスを初期化し、天気データ取得状態の変更購読を開始する。
        /// </summary>
        /// <param name="viewModel">メインウィンドウの ViewModel。</param>
        /// <param name="settingsService">表示設定の読み込み・保存を行うサービス。</param>
        public MainWindow(MainViewModel viewModel, ISettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _viewModel = viewModel;
            DataContext = viewModel;

            _viewModel.Weather.PropertyChanged += Weather_PropertyChanged;
        }

        /// <summary>
        /// 天気データの有無が変化したときに、ウィンドウ位置を再計算する。
        /// </summary>
        /// <param name="sender">イベント送信元。</param>
        /// <param name="e">プロパティ変更イベント引数。</param>
        private void Weather_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WeatherViewModel.HasData))
            {
                WindowPositionHelper.Apply(this, _settingsService.Load(), _viewModel.Weather.HasData);
            }
        }

        /// <summary>
        /// ウィンドウハンドル初期化後にクリックスルー設定と Alt+Tab 除外を適用する。
        /// </summary>
        /// <param name="e">イベント引数。</param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var windowHandle = new WindowInteropHelper(this).Handle;
            WindowClickThroughHelper.MakeClickThrough(windowHandle);
            WindowClickThroughHelper.ExcludeFromAltTab(windowHandle);
        }
    }
}