using System.Windows;
using MuseWiser.ViewModels;

namespace MuseWiser
{
    /// <summary>
    /// 設定画面のウィンドウ。配置・サイズ・天気・最前面・自動起動設定を編集する。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>
        /// <see cref="SettingsWindow"/> の新しいインスタンスを初期化し、閉じる要求の購読を開始する。
        /// </summary>
        /// <param name="viewModel">設定画面の ViewModel。</param>
        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
