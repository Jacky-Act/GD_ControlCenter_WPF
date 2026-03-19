using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Services;
using System;
using System.Windows;

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class ScriptSaveViewModel : ObservableObject
    {
        private readonly JsonConfigService _configService;
        private readonly Action _closeAction;
        private readonly Action _toggleSaveAction;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotSaving))]
        [NotifyPropertyChangedFor(nameof(ToggleBtnText))]
        [NotifyPropertyChangedFor(nameof(ToggleBtnColor))]
        private bool _isScriptSaving;

        public bool IsNotSaving => !IsScriptSaving;
        public string ToggleBtnText => IsScriptSaving ? "停止保存" : "启动保存";
        public string ToggleBtnColor => IsScriptSaving ? "#F44336" : "#673ab7"; // 运行中显红色，否则为主色调

        [ObservableProperty] private int _scriptSaveCount;
        [ObservableProperty] private int _scriptSaveInterval;
        [ObservableProperty] private string _scriptSaveDirectory = string.Empty;

        public ScriptSaveViewModel(
            JsonConfigService configService,
            AppConfig currentConfig,
            bool isCurrentlySaving,
            Action closeAction,
            Action toggleSaveAction)
        {
            _configService = configService;
            _closeAction = closeAction;
            _toggleSaveAction = toggleSaveAction;
            IsScriptSaving = isCurrentlySaving;

            // 初始化参数
            ScriptSaveCount = currentConfig.ScriptSaveCount > 0 ? currentConfig.ScriptSaveCount : 100;
            ScriptSaveInterval = currentConfig.ScriptSaveInterval > 0 ? currentConfig.ScriptSaveInterval : 5;
            ScriptSaveDirectory = string.IsNullOrWhiteSpace(currentConfig.ScriptSaveDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : currentConfig.ScriptSaveDirectory;
        }

        [RelayCommand] private void IncreaseCount() { if (ScriptSaveCount < 10000) ScriptSaveCount++; }
        [RelayCommand] private void DecreaseCount() { if (ScriptSaveCount > 1) ScriptSaveCount--; }
        [RelayCommand] private void IncreaseInterval() { if (ScriptSaveInterval < 3600) ScriptSaveInterval++; }
        [RelayCommand] private void DecreaseInterval() { if (ScriptSaveInterval > 0) ScriptSaveInterval--; }

        [RelayCommand]
        private void BrowseDirectory()
        {
            // 使用 WinForms 的文件夹选择对话框 (适用于 .NET 6/7)
            // 注意：请确保项目的 .csproj 文件中包含 <UseWindowsForms>true</UseWindowsForms>
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择脚本保存目录",
                UseDescriptionForTitle = true,
                SelectedPath = ScriptSaveDirectory
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ScriptSaveDirectory = dialog.SelectedPath;
            }
        }

        private bool SaveConfigToLocal()
        {
            if (ScriptSaveCount < 1 || ScriptSaveInterval <= 0)
            {
                MessageBox.Show("次数和间隔必须大于 0", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var config = _configService.Load();
            config.ScriptSaveCount = ScriptSaveCount;
            config.ScriptSaveInterval = ScriptSaveInterval;
            config.ScriptSaveDirectory = ScriptSaveDirectory;
            _configService.Save(config);
            return true;
        }

        [RelayCommand]
        private void Apply()
        {
            if (SaveConfigToLocal())
            {
                _closeAction?.Invoke();
            }
        }

        [RelayCommand]
        private void ToggleSave()
        {
            // 如果未在运行，先校验并保存配置，再启动
            if (!IsScriptSaving)
            {
                if (!SaveConfigToLocal()) return;
            }

            // 触发主界面的启停逻辑，并关闭窗口
            _toggleSaveAction?.Invoke();
            _closeAction?.Invoke();
        }
    }
}