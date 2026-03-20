using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GD_ControlCenter_WPF.Models;
using GD_ControlCenter_WPF.Models.Messages;
using GD_ControlCenter_WPF.Services;
using System.Windows;

/*
 * 文件名: ScriptSaveViewModel.cs
 * 模块: 视图模型层 (UI ViewModels)
 * 描述: 自动化脚本保存配置弹窗的后台逻辑。
 * 架构职责:
 * 1. 独立管理脚本保存的参数配置（次数、间隔、路径）。
 * 2. 负责将合法的配置持久化到本地 JSON。
 * 3. 【跨窗口同步】：通过监听全局消息，确保在窗口打开期间，UI 状态能与主界面的后台采集任务保持绝对同步。
 */

namespace GD_ControlCenter_WPF.ViewModels.Dialogs
{
    public partial class ScriptSaveViewModel : ObservableObject
    {
        #region 1. 依赖注入与回调行为

        private readonly JsonConfigService _configService;

        /// <summary>
        /// 窗口关闭回调委托。由 View 层在实例化时注入，用于在 ViewModel 中控制 UI 窗口的关闭。
        /// </summary>
        private readonly Action _closeAction;

        /// <summary>
        /// 启停主界面保存逻辑的回调委托。
        /// </summary>
        private readonly Action _toggleSaveAction;

        #endregion

        #region 2. 核心运行状态与 UI 动态绑定

        /// <summary>
        /// 当前是否正在执行后台脚本保存任务。
        /// 通过 NotifyPropertyChangedFor 级联更新关联的文本、颜色和控件可用性。
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotSaving))]
        [NotifyPropertyChangedFor(nameof(ToggleBtnText))]
        [NotifyPropertyChangedFor(nameof(ToggleBtnColor))]
        private bool _isScriptSaving;

        /// <summary>
        /// 取反状态：用于控制参数输入框在“运行中”被锁定 (IsEnabled = false)。
        /// </summary>
        public bool IsNotSaving => !IsScriptSaving;

        /// <summary>
        /// 动态启停按钮文本。
        /// </summary>
        public string ToggleBtnText => IsScriptSaving ? "停止保存" : "启动保存";

        /// <summary>
        /// 动态启停按钮颜色。运行中变红 (#F44336)，否则保持主色调 (#673ab7)。
        /// </summary>
        public string ToggleBtnColor => IsScriptSaving ? "#F44336" : "#673ab7";

        #endregion

        #region 3. 脚本存储配置参数

        [ObservableProperty]
        private int _scriptSaveCount;

        [ObservableProperty]
        private int _scriptSaveInterval;

        [ObservableProperty]
        private string _scriptSaveDirectory = string.Empty;

        #endregion

        #region 4. 初始化与跨窗口消息监听

        /// <summary>
        /// 实例化脚本保存视图模型。
        /// </summary>
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

            // --- 初始参数安全校验与加载 ---
            // 防呆：防止配置文件损坏导致出现 0 或负数
            ScriptSaveCount = currentConfig.ScriptSaveCount > 0 ? currentConfig.ScriptSaveCount : 100;
            ScriptSaveInterval = currentConfig.ScriptSaveInterval > 0 ? currentConfig.ScriptSaveInterval : 5;

            // 如果路径为空，默认回退到系统桌面
            ScriptSaveDirectory = string.IsNullOrWhiteSpace(currentConfig.ScriptSaveDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : currentConfig.ScriptSaveDirectory;

            // --- 【核心机制：跨窗口状态同步】 ---
            // 场景说明：当该弹窗处于打开状态时，主界面的后台保存任务可能刚好执行完毕并自动停止。
            // 此时主界面会通过 WeakReferenceMessenger 广播 ScriptSaveStateChangedMessage。
            // 本弹窗监听到该消息后，立即同步自身的 IsScriptSaving，从而让变红的按钮瞬间恢复原状。
            WeakReferenceMessenger.Default.Register<ScriptSaveStateChangedMessage>(this, (r, m) =>
            {
                // 确保在主 UI 线程安全更新属性
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsScriptSaving = m.Value;
                });
            });
        }

        #endregion

        #region 5. 参数增减控制 (安全防溢出)

        // 这里的指令直接绑定到 UI 上的 +/- 按钮，严格控制物理参数的逻辑边界

        [RelayCommand]
        private void IncreaseCount() { if (ScriptSaveCount < 10000) ScriptSaveCount++; }

        [RelayCommand]
        private void DecreaseCount() { if (ScriptSaveCount > 1) ScriptSaveCount--; }

        [RelayCommand]
        private void IncreaseInterval() { if (ScriptSaveInterval < 3600) ScriptSaveInterval++; } // 最大间隔 1 小时

        [RelayCommand]
        private void DecreaseInterval() { if (ScriptSaveInterval > 1) ScriptSaveInterval--; } // 修复：间隔不能为 0，最小为 1 秒

        #endregion

        #region 6. 本地配置读写与业务提交

        /// <summary>
        /// 调用系统对话框浏览并选择目标文件夹。
        /// </summary>
        [RelayCommand]
        private void BrowseDirectory()
        {
            // 使用 WinForms 的原生文件夹选择器 (需在 .csproj 中开启 <UseWindowsForms>true</UseWindowsForms>)
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

        /// <summary>
        /// 将当前 UI 上的参数持久化到本地 JSON 配置文件中。
        /// </summary>
        /// <returns>校验并保存成功返回 true，参数非法返回 false。</returns>
        private bool SaveConfigToLocal()
        {
            if (ScriptSaveCount < 1 || ScriptSaveInterval <= 0)
            {
                MessageBox.Show("保存次数和间隔必须大于 0", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var config = _configService.Load();
            config.ScriptSaveCount = ScriptSaveCount;
            config.ScriptSaveInterval = ScriptSaveInterval;
            config.ScriptSaveDirectory = ScriptSaveDirectory;
            _configService.Save(config);

            return true;
        }

        /// <summary>
        /// 单独点击“应用”按钮：仅保存参数并关闭窗口，不触发采集逻辑。
        /// </summary>
        [RelayCommand]
        private void Apply()
        {
            if (SaveConfigToLocal())
            {
                _closeAction?.Invoke();
            }
        }

        /// <summary>
        /// 点击“启动/停止”大按钮：保存参数，触发主界面任务，并关闭窗口。
        /// </summary>
        [RelayCommand]
        private void ToggleSave()
        {
            // 逻辑门：只有在“准备启动”的情况下，才需要校验并覆写本地配置
            // 如果是“正在录制”要去“停止”，则无需校验，直接放行
            if (!IsScriptSaving)
            {
                if (!SaveConfigToLocal()) return;
            }

            // 触发主控面板的启停委托
            _toggleSaveAction?.Invoke();

            // 关闭当前弹窗
            _closeAction?.Invoke();
        }

        #endregion
    }
}